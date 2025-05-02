using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SlackNet;
using SlackNet.WebApi;

namespace ImpowerSurvey.Tests.Services
{
    // Direct implementation of SlackService for testing
    public class TestSlackService : SlackService
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

        // Constructor for test implementation
        public TestSlackService(ILogService logService)
            : base(CreateMockWebHostEnvironment(), CreateMockSlackClient(), CreateMockConfiguration(), 
                  CreateMockUserService(), logService)
        {
            _logService = logService;
        }
        
        // Create minimal mock versions of required dependencies
        private static IWebHostEnvironment CreateMockWebHostEnvironment()
        {
            var mock = new Mock<IWebHostEnvironment>();
            mock.Setup(e => e.EnvironmentName).Returns("Development");
            return mock.Object;
        }
        
        private static ISlackApiClient CreateMockSlackClient()
        {
            var mockSlackClient = new Mock<ISlackApiClient>();
            
            // Mock the Users service
            var mockUsersApi = new Mock<IUsersApi>();
            
            // Mock the LookupByEmail method to return a user
            mockUsersApi
                .Setup(u => u.LookupByEmail(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SlackNet.User { 
                    Id = "U12345", 
                    Name = "testuser",
                    RealName = "Test User"
                });
                
            // Assign the mocked Users API to the SlackClient
            mockSlackClient.Setup(c => c.Users).Returns(mockUsersApi.Object);
            
            // Mock the Auth API
            var mockAuthApi = new Mock<IAuthApi>();
            mockAuthApi
                .Setup(a => a.Test(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthTestResponse 
                { 
                    UserId = "U12345", 
                    User = "testbot", 
                    Team = "Test Team" 
                });
                
            // Assign the mocked Auth API to the SlackClient
            mockSlackClient.Setup(c => c.Auth).Returns(mockAuthApi.Object);
            
            // Mock the Chat API
            var mockChatApi = new Mock<IChatApi>();
            mockChatApi
                .Setup(c => c.PostMessage(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PostMessageResponse { Ts = "12345.67890" });
                
            // Assign the mocked Chat API to the SlackClient
            mockSlackClient.Setup(c => c.Chat).Returns(mockChatApi.Object);
            
            return mockSlackClient.Object;
        }
        
        private static IConfiguration CreateMockConfiguration()
        {
            var mock = new Mock<IConfiguration>();
            mock.Setup(c => c["URLS"]).Returns("https://localhost:5001");
            return mock.Object;
        }
        
        private static UserService CreateMockUserService()
        {
            var mockDbContextFactory = new Mock<IDbContextFactory<SurveyDbContext>>();
            var mockLogger = new Mock<ILogger<LogService>>();
            var logService = new LogService(mockDbContextFactory.Object, mockLogger.Object);
            return new Mock<UserService>(mockDbContextFactory.Object, logService).Object;
        }

        // Completely replace base method with test implementation
        public Task<int> SendBulkMessages(List<string> emails, string message, string context = "bulk message")
        {
            // Record the call for verification
            SentBulkMessages.Add((emails, message, context));
            
            // Log the action for debugging
            _logService?.LogAsync(LogSource.SlackService, LogLevel.Warning,
                $"TEST: SendBulkMessages called with {emails?.Count ?? 0} recipients: {context}").GetAwaiter().GetResult();
            
            // Skip actual implementation and just return success count
            return Task.FromResult(BulkMessageReturnValue);
        }

        // Completely replace base method with test implementation
        public Task<bool> SendSurveyInvitation(string email, Guid surveyId, string surveyTitle, string surveyManager, string entryCode)
        {
            // Record the call for verification
            SentInvitations.Add((email, surveyId, surveyTitle, surveyManager, entryCode));
            
            // Log the action for debugging
            _logService?.LogAsync(LogSource.SlackService, LogLevel.Warning,
                $"TEST: SendSurveyInvitation called for {email}, survey: {surveyTitle}").GetAwaiter().GetResult();
            
            // Skip actual implementation and just return success
            return Task.FromResult(InvitationReturnValue);
        }

        // Completely replace base method with test implementation
        public new Task Notify(string message, params Roles[] roles)
        {
            // Record the call for verification
            SentNotifications.Add((message, roles));
            
            // Log the action for debugging
            _logService?.LogAsync(LogSource.SlackService, LogLevel.Warning,
                $"TEST: Notify called with message: {message}").GetAwaiter().GetResult();
            
            // Skip actual implementation
            return Task.CompletedTask;
        }

        // Completely replace base method with test implementation
        public new Task<List<string>> VerifyParticipants(List<string> participants)
        {
            // Record the call for verification
            VerifiedParticipants.Add(participants);
            
            // Log the action for debugging
            _logService?.LogAsync(LogSource.SlackService, LogLevel.Warning,
                $"TEST: VerifyParticipants called with {participants?.Count ?? 0} participants").GetAwaiter().GetResult();
            
            // Skip actual implementation and return empty list (means all participants are valid)
            return Task.FromResult(VerifyParticipantsReturnValue);
        }

        // Completely replace base method with test implementation
        public new Task StartAsync(CancellationToken cancellationToken)
        {
            // Skip actual implementation
            return Task.CompletedTask;
        }

        // Completely replace base method with test implementation
        public new Task StopAsync(CancellationToken cancellationToken)
        {
            // Skip actual implementation
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