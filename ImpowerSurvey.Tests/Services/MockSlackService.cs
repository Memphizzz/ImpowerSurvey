using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    /// <summary>
    /// Mock implementation of ISlackService for unit testing
    /// </summary>
    public class MockSlackService : ISlackService
    {
        // Records of method calls for verification
        public List<(List<string> Recipients, string Message, string Context)> SentBulkMessages { get; } = new();
        public List<(string Email, Guid SurveyId, string Title, string Manager, string EntryCode)> SentInvitations { get; } = new();
        public List<(string Message, Roles[] Roles)> SentNotifications { get; } = new();
        public List<List<string>> VerifiedParticipants { get; } = new();
        
        // Return values for test verification
        public int BulkMessageReturnValue { get; set; } = 0;
        public bool InvitationReturnValue { get; set; } = true;
        public List<string> VerifyParticipantsReturnValue { get; set; } = new();

        private readonly ILogService _logService;

        public MockSlackService(ILogService logService)
        {
            _logService = logService;
        }

        public async Task<int> SendBulkMessages(List<string> emails, string message, string context = "bulk message", string timeZone = null)
        {
            // Record the call for verification
            SentBulkMessages.Add((emails, message, context));
            
            if (emails == null || !emails.Any())
                return 0;
                
            // Log the action for test visibility
            await _logService?.LogAsync(LogSource.SlackService, LogLevel.Information, 
                $"[MOCK] SendBulkMessages called with {emails?.Count ?? 0} recipients: {context}");
            
            // Just return the configured success count for testing
            return BulkMessageReturnValue;
        }

        public async Task<bool> SendSurveyInvitation(string email, Guid surveyId, string surveyTitle, User surveyManager, string entryCode)
        {
            // Record the call for verification
            SentInvitations.Add((email, surveyId, surveyTitle, surveyManager.DisplayName, entryCode));
            
            // Log the action for test visibility
            await _logService?.LogAsync(LogSource.SlackService, LogLevel.Information, 
                $"[MOCK] SendSurveyInvitation called for {email}, survey: {surveyTitle}");
            
            // Return configured result
            return InvitationReturnValue;
        }

        public async Task Notify(string message, params Roles[] roles)
        {
            // Record the call for verification
            SentNotifications.Add((message, roles));
            
            // Log the action for test visibility
            await _logService?.LogAsync(LogSource.SlackService, LogLevel.Information, 
                $"[MOCK] Notify called with message: {message}");
        }

        public async Task<List<string>> VerifyParticipants(List<string> participants)
        {
            // Record the call for verification
            VerifiedParticipants.Add(participants);
            
            // Log the action for test visibility
            await _logService?.LogAsync(LogSource.SlackService, LogLevel.Information, 
                $"[MOCK] VerifyParticipants called with {participants?.Count ?? 0} participants");
            
            // Return configured invalid emails list
            return VerifyParticipantsReturnValue;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // No-op for test implementation
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // No-op for test implementation
            return Task.CompletedTask;
        }
        
        public void Reset()
        {
            SentBulkMessages.Clear();
            SentInvitations.Clear();
            SentNotifications.Clear();
            VerifiedParticipants.Clear();
            BulkMessageReturnValue = 0;
            InvitationReturnValue = true;
            // Empty list means all participants are valid
            VerifyParticipantsReturnValue = new List<string>();
        }
    }
}