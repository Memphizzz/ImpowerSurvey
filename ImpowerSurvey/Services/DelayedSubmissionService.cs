using ImpowerSurvey.Components.Model;
using Microsoft.EntityFrameworkCore;

namespace ImpowerSurvey.Services;

/// <summary>
/// Service that implements SHIELD privacy principles by providing delayed, randomized 
/// submission of survey responses to prevent correlation between participants and answers
/// </summary>
public class DelayedSubmissionService
(
	IDbContextFactory<SurveyDbContext> contextFactory, DssConfiguration config, ILogService logService,
	ILeaderElectionService leaderElectionService, IClaudeService claudeService,
	ISettingsService settingsService, IHttpClientFactory httpClientFactory,
	IConfiguration configuration) : IHostedService, IDisposable
{
	// WARNING: DO NOT CONVERT TO PRIMARY CONSTRUCTOR PARAMETERS!
	// This is a technical limitation with C# primary constructors when implementing interfaces:
	// 1. Primary constructor parameters are only correctly accessible from methods declared in the interface
	//    (e.g., StartAsync and StopAsync from IHostedService)
	// 2. When called from non-interface methods, the compiler-generated backing fields will be null at runtime
	// 3. This causes NullReferenceExceptions despite the code appearing syntactically correct
	// ReSharper incorrectly suggests converting these fields to primary constructor parameters, but doing so
	// would break functionality in all methods that aren't part of IHostedService.

	// ReSharper disable ReplaceWithPrimaryConstructorParameter
	private readonly IDbContextFactory<SurveyDbContext> _contextFactory = contextFactory;
	private readonly DssConfiguration _config = config;
	private readonly ILogService _logService = logService;
	private readonly ILeaderElectionService _leaderElectionService = leaderElectionService;
	private readonly IClaudeService _claudeService = claudeService;
	private readonly ISettingsService _settingsService = settingsService;
	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
	private readonly IConfiguration _configuration = configuration;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter

	/// <summary>
	/// Event triggered when the status of the delayed submission service changes
	/// </summary>
	public event Action<DelayedSubmissionStatus> OnStatusChanged;

	private readonly object _lock = new();
	private readonly List<Response> _pendingResponses = [];

	private int _lastDumpAmount, _currentPercentage;
	private DateTime _lastDumpTime, _nextDumpTime;
	private Timer _timer;

	private bool _hasTransferredResponses;
	private string _instanceSecret;

	private static readonly Random Random = new();

	/// <summary>
	/// Disposes resources used by the service
	/// </summary>
	public void Dispose()
	{
		_timer?.Dispose();
	}

	/// <summary>
	/// Starts the delayed submission service
	/// </summary>
	/// <param name="cancellationToken">Cancellation token</param>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		// Simple polling approach to wait for LeaderElectionService to be ready
		while (!_leaderElectionService.IsReady && !cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(250, cancellationToken);
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information,
									   "Waiting for leader election..");
		}

		if (cancellationToken.IsCancellationRequested)
			return;

		_currentPercentage = _config.MinPercentage;
		_leaderElectionService.OnLeadershipChanged += HandleLeadershipChanged;
		_instanceSecret = _configuration[Constants.App.EnvInstanceSecret] ??
						  Environment.GetEnvironmentVariable(Constants.App.EnvInstanceSecret, EnvironmentVariableTarget.Process);

		if (string.IsNullOrWhiteSpace(_instanceSecret))
			throw new Exception("Instance secret is not configured");

		// If we're already the leader, ensure the timer is started
		if (_leaderElectionService.IsLeader)
			await HandleLeadershipChangedAsync(true);
		else
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information, "Standing by..");
	}

	/// <summary>
	/// Gets all pending responses and clears the pending queue
	/// </summary>
	/// <returns>All pending responses</returns>
	private List<Response> GetAndClearPendingResponses()
	{
		lock (_lock)
		{
			var responses = _pendingResponses.ToList();
			_pendingResponses.Clear();
			return responses;
		}
	}

	/// <summary>
	/// Stops the delayed submission service
	/// </summary>
	/// <param name="cancellationToken">Cancellation token</param>
	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_timer?.Change(Timeout.Infinite, 0);

		// Attempt one final transfer of responses to leader if we're a follower
		if (!_leaderElectionService.IsLeader && _pendingResponses.Count > 0)
		{
			var responses = GetAndClearPendingResponses();
			if (responses.Count > 0)
				_ = TransferResponsesToLeaderAsync(responses);
		}
	}

	private void HandleLeadershipChanged(bool isLeader)
	{
		_ = HandleLeadershipChangedAsync(isLeader);
	}

	/// <summary>
	/// Handles leadership changes
	/// </summary>
	/// <param name="isLeader">Whether this instance is now the leader</param>
	private async Task HandleLeadershipChangedAsync(bool isLeader)
	{
		if (isLeader)
		{
			// We're now the leader
			lock (_lock)
			{
				// Ensure the timer is started regardless of pending responses
				SetRandomTimer();
			}

			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information,
									   "Started processing timer");
		}
		else
		{
			// We're no longer the leader
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information, "This instance is now a follower");

			// If we have any pending responses, transfer them immediately
			if (_pendingResponses.Count > 0)
			{
				var responses = GetAndClearPendingResponses();
				if (responses.Count > 0)
				{
					await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information,
											   $"Transferring {responses.Count} existing responses to new leader");
					_ = TransferResponsesToLeaderAsync(responses);
				}
			}
		}

		OnStatusChanged?.Invoke(GetStatus());
	}

	/// <summary>
	/// Transfers responses to the leader instance
	/// </summary>
	/// <param name="responses">The responses to transfer to the leader.</param>
	private async Task<bool> TransferResponsesToLeaderAsync(List<Response> responses)
	{
		// Skip if we're the leader
		if (_leaderElectionService.IsLeader)
			return true;

		// Skip if no responses to transfer
		if (responses == null || responses.Count == 0)
			return true;

		var leaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
		var leaderUrl = $"https://{leaderId}";

		// Log the transfer attempt
		await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Debug,
								   $"Attempting to transfer {responses.Count} responses to leader at: {leaderUrl}");

		try
		{
			// Use the factory extension method which handles creating the client with proper configuration
			var result = await _httpClientFactory.TransferResponsesToLeaderAsync(leaderUrl, _leaderElectionService.InstanceId, responses, _instanceSecret);
			if (result.Successful)
			{
				_hasTransferredResponses = true;

				await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information,
										   $"Successfully transferred {responses.Count} responses to leader");

				OnStatusChanged?.Invoke(GetStatus());
				return true;
			}

			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Warning,
									   $"Failed to transfer responses to leader: {result.Message}");
			return false;
		}
		catch (Exception ex)
		{
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Error,
									   $"Exception transferring responses to leader: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Sets a random timer for the next batch of response submissions
	/// </summary>
	private void SetRandomTimer()
	{
		var next = _timer == null ? TimeSpan.FromMinutes(Random.Next(15, 59)) : TimeSpan.FromSeconds(Random.Next(30, 90));
		_nextDumpTime = DateTime.UtcNow.Add(next);
		OnStatusChanged?.Invoke(GetStatus());

		// Dispose existing timer before creating a new one
		_timer?.Dispose();
		_timer = new Timer(PollAndSubmitResponses, null, next, Timeout.InfiniteTimeSpan);
	}

	/// <summary>
	/// Analyzes responses to calculate statistical discrepancies, used for research and quality control
	/// while maintaining SHIELD privacy
	/// </summary>
	/// <param name="responses">The list of responses to analyze</param>
	/// <returns>The analyzed responses</returns>
	private List<Response> AnalyzeResponses(List<Response> responses)
	{
		var ratingResponses = responses.Where(x => x.QuestionType == QuestionTypes.Rating).ToList();

		if (ratingResponses.Count == 0)
			return responses;

		var averageRating = CalculateAverageRating(ratingResponses);

		foreach (var response in ratingResponses)
			if (int.TryParse(response.Answer, out var rating))
				response.Discrepancy = Math.Round(rating - averageRating, 2);

		return responses;
	}

	/// <summary>
	/// Calculates the average rating from a list of rating responses
	/// </summary>
	/// <param name="ratingResponses">The list of rating responses</param>
	/// <returns>The average rating value</returns>
	private static double CalculateAverageRating(List<Response> ratingResponses)
	{
		var validRatings = ratingResponses
						   .Select(x => int.TryParse(x.Answer, out var rating) ? rating : (int?)null)
						   .Where(x => x.HasValue)
						   .Select(x => x.Value)
						   .ToList();

		return validRatings.Count > 0 ? validRatings.Average() : 0;
	}

	/// <summary>
	/// Queues survey responses for delayed submission
	/// </summary>
	/// <param name="responses">The list of responses to queue</param>
	public void QueueResponses(List<Response> responses)
	{
		if (responses.Count == 0)
			return;

		// If leader, queue the responses for delayed submission
		if (_leaderElectionService.IsLeader)
			lock (_lock)
			{
				_pendingResponses.AddRange(AnalyzeResponses(responses));
				OnStatusChanged?.Invoke(GetStatus());

				if (_timer == null)
					SetRandomTimer();
			}
		else
			// As a follower, transfer the responses to the leader immediately
			_ = TransferResponsesToLeaderAsync(AnalyzeResponses(responses));
	}

	private void PollAndSubmitResponses(object state)
	{
		_ = PollAndSubmitResponsesAsync(state);
	}

	private async Task PollAndSubmitResponsesAsync(object state)
	{
		if (!_leaderElectionService.IsLeader)
		{
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Debug,
									   $"Instance {_leaderElectionService.InstanceId} skipping processing as it's not the leader");
			return;
		}

		await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Debug,
								   $"Leader instance {_leaderElectionService.InstanceId} processing pending submissions");
		List<Response> responsesToSubmit = [];

		try
		{
			await using var dbContext = await _contextFactory.CreateDbContextAsync();

			lock (_lock)
			{
				var surveyGroups = _pendingResponses.GroupBy(x => x.SurveyId).ToList();

				foreach (var group in surveyGroups)
				{
					var surveyId = group.Key;
					var surveyResponses = group.ToList();

					var questionCount = dbContext.Questions.Count(x => x.SurveyId == surveyId);
					var minimumResponses = questionCount * _config.MinimumSurveySubmissions;

					if (surveyResponses.Count > minimumResponses)
					{
						var amountToSubmit = Math.Min((int)Math.Ceiling(surveyResponses.Count * (_currentPercentage / 100.0)),
													  surveyResponses.Count - minimumResponses);
						var selectedResponses = surveyResponses
												.OrderBy(_ => Random.Next())
												.Take(amountToSubmit)
												.ToList();

						foreach (var response in selectedResponses)
							_pendingResponses.Remove(response);

						responsesToSubmit.AddRange(AnalyzeResponses(selectedResponses));
					}
				}
			}

			if (responsesToSubmit.Count > 0)
			{
				await SubmitResponses(responsesToSubmit);
				OnStatusChanged?.Invoke(GetStatus());
				UpdateSubmissionPercentage();
			}
		}
		catch (Exception ex)
		{
			// Log through LogService with special care for SHIELD compliance
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Error, "Exception in DSS (details omitted due to SHIELD compliance)");
		}
		finally
		{
			lock (_lock)
			{
				if (responsesToSubmit.Count > 0)
					SetRandomTimer();
				else
				{
					_nextDumpTime = default;
					_timer?.Dispose();
					_timer = null;

					_currentPercentage = _config.MinPercentage;
					OnStatusChanged?.Invoke(GetStatus());
				}
			}
		}
	}

	private void UpdateSubmissionPercentage()
	{
		_currentPercentage = Random.Next(100) < _config.ResetChancePercentage ? _config.MinPercentage : Math.Min(_currentPercentage + _config.PercentageIncrement, _config.MaxPercentage);
	}

	/// <summary>
	/// Submits responses to the database and updates statistics
	/// </summary>
	/// <param name="responses">The list of responses to submit</param>
	private async Task SubmitResponses(List<Response> responses)
	{
		// Anonymize text responses
		var textResponses = responses.Where(r => r.QuestionType == QuestionTypes.Text).ToList();
		if (textResponses.Count > 0)
		{
			try
			{
				foreach (var response in textResponses.Where(response => !string.IsNullOrWhiteSpace(response.Answer)))
					response.Answer = await _claudeService.AnonymizeTextAsync(response.Answer);
			}
			catch (Exception ex)
			{
				// If anonymization fails, log it but continue with submission
				// We don't want to block the submission process if Claude is unavailable
				await _logService.LogExceptionAsync(ex, LogSource.DelayedSubmissionService, "Response Anonymization", true, true);
				await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Warning,
										   "Text anonymization failed, proceeding with original text");
			}
		}

		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		await dbContext.Responses.AddRangeAsync(responses);
		await dbContext.SaveChangesAsync();
		_lastDumpTime = DateTime.UtcNow;
		_lastDumpAmount = responses.Count;
	}

	/// <summary>
	/// Gets the current status of the delayed submission service
	/// </summary>
	/// <returns>A status object containing information about pending submissions</returns>
	public DelayedSubmissionStatus GetStatus() =>
		new()
		{
			Pending = _pendingResponses.Count,
			LastTime = _lastDumpTime,
			LastAmount = _lastDumpAmount,
			CurrentPercentage = _currentPercentage,
			NextTime = _nextDumpTime,
			IsLeader = _leaderElectionService.IsLeader,
			InstanceId = _leaderElectionService.InstanceId,
			HasTransferredResponses = _hasTransferredResponses
		};

	/// <summary>
	/// Immediately submits all pending responses for a specific survey
	/// </summary>
	/// <param name="surveyId">The ID of the survey to flush responses for</param>
	/// <returns>The number of responses submitted</returns>
	public async Task<DataServiceResult<int>> FlushPendingResponses(Guid surveyId)
	{
		try
		{
			// Only the leader should flush responses
			if (!_leaderElectionService.IsLeader)
			{
				await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Warning,
										   $"Non-leader instance {_leaderElectionService.InstanceId} attempted to flush responses for survey {surveyId}");
				return ServiceResult.Failure<int>($"This instance ({_leaderElectionService.InstanceId}) is not the leader");
			}

			List<Response> responsesToSubmit;
			lock (_lock)
			{
				responsesToSubmit = _pendingResponses
									.Where(r => r.SurveyId == surveyId)
									.OrderBy(_ => Random.Next())
									.ToList();

				if (responsesToSubmit.Count == 0)
					return ServiceResult.Success(0, $"No pending responses found for survey ID {surveyId}");

				_pendingResponses.RemoveAll(r => r.SurveyId == surveyId);
			}

			if (responsesToSubmit.Count > 0)
			{
				await SubmitResponses(responsesToSubmit);
				await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information,
										   $"Flushed {responsesToSubmit.Count} responses for survey ID {surveyId}");
			}

			OnStatusChanged?.Invoke(GetStatus());
			return ServiceResult.Success(responsesToSubmit.Count, $"Successfully flushed {responsesToSubmit.Count} responses for survey ID {surveyId}");
		}
		catch (Exception ex)
		{
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Error,
									   $"Error flushing responses for survey ID {surveyId}: {ex.Message}");
			return ServiceResult.Failure<int>($"Error flushing responses: {ex.Message}");
		}
	}

#if DEBUG
	/// <summary>
	/// [DEBUG ONLY] Forces immediate submission of all pending responses
	/// This method is only available in debug builds and should never be used in production
	/// as it bypasses SHIELD privacy guarantees
	/// </summary>
	/// <returns>The number of responses submitted</returns>
	public async Task<int> ForceFlushAllPendingResponses()
	{
		// Only the leader should flush responses
		if (!_leaderElectionService.IsLeader)
		{
			await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Warning,
									   $"Non-leader instance {_leaderElectionService.InstanceId} attempted to force flush all responses");
			return 0;
		}

		await _logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Warning,
								   "DEVELOPER NOTICE: Force flushing all pending responses - THIS SHOULD NEVER HAPPEN IN PRODUCTION!");

		var responses = GetAndClearPendingResponses();
		if (responses.Count > 0)
			await SubmitResponses(responses);

		OnStatusChanged?.Invoke(GetStatus());
		return responses.Count;
	}
#endif
}
