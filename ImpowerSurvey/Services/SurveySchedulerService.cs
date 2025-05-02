using ImpowerSurvey.Components.Model;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ImpowerSurvey.Services;

/// <summary>
/// Background service that manages scheduled survey operations including 
/// starting surveys, closing surveys, and sending reminders to participants
/// </summary>
public class SurveySchedulerService(
    IDbContextFactory<SurveyDbContext> contextFactory, 
    SurveyService surveyService,
    ISlackService slackService,
    ISettingsService settingsService,
    ILogService logService,
    ILeaderElectionService leaderElectionService) : BackgroundService
{
    // WARNING: DO NOT CONVERT TO PRIMARY CONSTRUCTOR PARAMETERS!
    // This is a technical limitation with C# primary constructors when implementing interfaces:
    // 1. Primary constructor parameters are only correctly accessible from methods declared in the interface
    //    (e.g., ExecuteAsync from BackgroundService)
    // 2. When called from non-interface methods, the compiler-generated backing fields will be null at runtime
    // 3. This causes NullReferenceExceptions despite the code appearing syntactically correct
    // ReSharper incorrectly suggests converting these fields to primary constructor parameters, but doing so
    // would break functionality in all methods that aren't part of IHostedService.

    // ReSharper disable ReplaceWithPrimaryConstructorParameter
    private readonly IDbContextFactory<SurveyDbContext> _contextFactory = contextFactory;
    private readonly SurveyService _surveyService = surveyService;
    private readonly ISlackService _slackService = slackService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly ILogService _logService = logService;
    private readonly ILeaderElectionService _leaderElectionService = leaderElectionService;
    // ReSharper restore ReplaceWithPrimaryConstructorParameter

    private readonly Dictionary<Guid, Timer> _surveyTimers = new();
    private readonly Dictionary<Guid, bool> _reminders = new();
    private int _checkIntervalHours;
    private int _lookAheadHours;

    /// <summary>
    /// Executes the background service, checking for and scheduling upcoming surveys and reminders
    /// </summary>
    /// <param name="stoppingToken">The token to monitor for cancellation requests</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the LeaderElectionService to be ready before proceeding
        await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Information,
                                  "Waiting for leader election...");
                                  
        // Simple polling approach to wait for LeaderElectionService to be ready
        while (!_leaderElectionService.IsReady && !stoppingToken.IsCancellationRequested)
			await Task.Delay(100, stoppingToken);

		if (stoppingToken.IsCancellationRequested)
            return;
		
		await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Information, "Starting up..");
                                  
        _checkIntervalHours = await _settingsService.GetIntSettingAsync(Constants.SettingsKeys.SchedulerCheckIntervalHours, 1);
        _lookAheadHours = await _settingsService.GetIntSettingAsync(Constants.SettingsKeys.SchedulerLookAheadHours, 24);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Only perform actions if this is the leader instance
            if (_leaderElectionService.IsLeader)
            {
                await ScheduleUpcomingSurveys();
                await SendSlackReminders();
            }
            
            await Task.Delay(TimeSpan.FromHours(_checkIntervalHours), stoppingToken);
        }
    }

    /// <summary>
    /// Finds upcoming surveys that need to be scheduled for state changes
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Accessed via reflection in SurveySchedulerServiceTests")]
    private async Task ScheduleUpcomingSurveys()
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync();
        var currentTime = DateTime.UtcNow;
        var maxScheduleTime = currentTime.AddHours(_lookAheadHours);

        var upcomingSurveys = await dbContext.Surveys
                                             .Where(x => (x.State == SurveyStates.Scheduled && x.ScheduledStartDate <= maxScheduleTime) ||
                                                         (x.State == SurveyStates.Running && x.ScheduledEndDate <= maxScheduleTime))
                                             .ToListAsync();

        foreach (var survey in upcomingSurveys)
            ScheduleSurveyStateChange(survey, currentTime);
    }

    /// <summary>
    /// Schedules appropriate timers for state changes (start/end) based on survey's current state
    /// </summary>
    /// <param name="survey">The survey to schedule state changes for</param>
    /// <param name="currentTime">The current time to compare against scheduled dates</param>
    private void ScheduleSurveyStateChange(Survey survey, DateTime currentTime)
    {
        switch (survey.State)
        {
            case SurveyStates.Scheduled when survey.ScheduledStartDate.HasValue && survey.ScheduledStartDate.Value > currentTime:
                ScheduleTimer(survey.Id, survey.ScheduledStartDate.Value, () => _ = ChangeSurveyState(survey.Id, SurveyStates.Running));
                break;

            case SurveyStates.Running when survey.ScheduledEndDate.HasValue && survey.ScheduledEndDate.Value > currentTime:
                ScheduleTimer(survey.Id, survey.ScheduledEndDate.Value, () => _ = ChangeSurveyState(survey.Id, SurveyStates.Closed));
                break;
        }
    }

    /// <summary>
    /// Creates and schedules a timer for a survey state change
    /// </summary>
    /// <param name="surveyId">The ID of the survey</param>
    /// <param name="targetTime">The time at which the state change should occur</param>
    /// <param name="action">The action to execute when the timer fires</param>
    private void ScheduleTimer(Guid surveyId, DateTime targetTime, Action action)
    {
        var delay = targetTime - DateTime.UtcNow;
        if (delay <= TimeSpan.Zero || delay > TimeSpan.FromHours(_lookAheadHours))
            return;

        if (_surveyTimers.TryGetValue(surveyId, out var existingTimer))
            existingTimer.Dispose();

        var timer = new Timer(_ =>
        {
            action();
            _surveyTimers.Remove(surveyId);
        }, null, delay, Timeout.InfiniteTimeSpan);

        _surveyTimers[surveyId] = timer;
    }

    /// <summary>
    /// Changes a survey's state and performs any additional actions needed based on the new state
    /// </summary>
    /// <param name="surveyId">The ID of the survey whose state to change</param>
    /// <param name="newState">The new state for the survey</param>
    [ExcludeFromCodeCoverage(Justification = "Accessed via reflection in SurveySchedulerServiceTests")]
    private async Task ChangeSurveyState(Guid surveyId, SurveyStates newState)
    {
        // Skip if we're no longer the leader (could happen if leadership changed while timer was active)
        if (!_leaderElectionService.IsLeader)
        {
            await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Warning, 
                $"Non-leader instance {_leaderElectionService.InstanceId} attempted to change survey state for {surveyId}");
            return;
        }
        
        await using var dbContext = await _contextFactory.CreateDbContextAsync();

        var survey = await dbContext.Surveys.FindAsync(surveyId);
        if (survey != null)
        {
            survey.State = newState;
            await dbContext.SaveChangesAsync();
            switch (newState)
            {
                case SurveyStates.Running:
                    if (survey.ParticipationType != ParticipationTypes.Manual)
                    {
                        var participants = survey.Participants;
                        survey.Participants.Clear();
                        await dbContext.SaveChangesAsync();
                        _ = _surveyService.SendInvites(surveyId, participants);
                    }
                    break;

                case SurveyStates.Closed:
                    _ = _surveyService.CloseSurvey(surveyId);
                    _reminders.Remove(surveyId);
                    break;

                case SurveyStates.Created:
                case SurveyStates.Scheduled:
                default:
                    throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
            }
        }
    }
    
    /// <summary>
    /// Sends reminder messages to participants who haven't completed their surveys
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Accessed via reflection in SurveySchedulerServiceTests")]
    private async Task SendSlackReminders()
    {
        // Skip if we're no longer the leader
        if (!_leaderElectionService.IsLeader)
            return;
            
        try
        {
            var remindersEnabled = await _settingsService.GetBoolSettingAsync(Constants.SettingsKeys.SlackReminderEnabled, true);
            if (!remindersEnabled)
                return;
                
            var reminderHoursBefore = await _settingsService.GetIntSettingAsync(Constants.SettingsKeys.SlackReminderHoursBefore, 24);
            if (reminderHoursBefore <= 0)
                return;
                
            var reminderTemplate = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.SlackReminderTemplate, 
                                                                               "Don't forget to complete the survey '{SurveyTitle}'. It closes in {HoursLeft} hours!");
                
            await using var dbContext = await _contextFactory.CreateDbContextAsync();
            var currentTime = DateTime.UtcNow;
            
            // Find all surveys that are running and have an end date
            var runningSurveys = await dbContext.Surveys
												.AsSplitQuery()
												.Include(s => s.Manager)
												.Include(s => s.CompletionCodes)
												.Where(s => s.State == SurveyStates.Running && s.ScheduledEndDate.HasValue)
												.ToListAsync();

            foreach (var survey in runningSurveys)
            {
                if (survey.ScheduledEndDate == null)
                    continue;
                
                var hoursUntilEnd = (survey.ScheduledEndDate.Value - currentTime).TotalHours;
                
                if (!(hoursUntilEnd <= reminderHoursBefore) || 
                    !(hoursUntilEnd > 0) ||
                    (_reminders.ContainsKey(survey.Id) && _reminders[survey.Id]))
                    continue;

                try
                {
                    // Get list of completion codes to find who completed the survey
                    var completedParticipantEmails = await dbContext.ParticipationRecords
                                                                    .Where(r => r.CompletionCode.SurveyId == survey.Id)
                                                                    .Select(r => r.UsedBy)
                                                                    .ToListAsync();
                        
                    // Find participants who haven't completed the survey yet
                    var pendingParticipants = survey.Participants.Where(p => !string.IsNullOrEmpty(p) && !completedParticipantEmails.Contains(p)).ToList();
                            
                    if (pendingParticipants.Any())
                    {
                        var message = reminderTemplate.Replace("{SurveyTitle}", survey.Title)
                                                      .Replace("{HoursLeft}", Math.Ceiling(hoursUntilEnd).ToString(CultureInfo.InvariantCulture));
                            
                        var remindersSent = await _slackService.SendBulkMessages(pendingParticipants, message, $"survey reminder for '{survey.Title}'", survey.Manager.TimeZone);
                            
                        await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Information, 
                                                   $"Reminder process completed for survey: {survey.Title} (ID: {survey.Id}), " +
                                                   $"Reminders sent: {remindersSent}/{pendingParticipants.Count}");
                    }
                    else
                    {
                        await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Information, 
                                                   $"No pending participants for survey: {survey.Title} (ID: {survey.Id})");
                    }
                }
                catch (Exception ex)
                {
                    await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Error, 
                                               $"Error processing reminders for survey {survey.Id}: {ex.Message}");
                }
                // Mark as sent even if there was an error
                _reminders[survey.Id] = true;
            }
        }
        catch (Exception ex)
        {
            await _logService.LogAsync(LogSource.SurveySchedulerService, LogLevel.Error, 
                $"Error checking for survey reminders: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the background service and cleans up any active timers
    /// </summary>
    /// <param name="stoppingToken">The token to monitor for cancellation requests</param>
    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        // Dispose all timers
        foreach (var timer in _surveyTimers.Values)
            await timer.DisposeAsync();
        
        _surveyTimers.Clear();
        await base.StopAsync(stoppingToken);
    }
}