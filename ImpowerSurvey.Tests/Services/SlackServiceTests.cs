using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class SlackServiceTests
    {
        private TestSlackService _testSlackService;
        private SurveyDbContext _dbContext;
        private ILogService _logService;
        private ILogger<LogService> _logger;
        private DbContextOptions<SurveyDbContext> _options;
        private Mock<IDbContextFactory<SurveyDbContext>> _mockContextFactory;
        private TestContextLoggerFactory _loggerFactory;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void TestInitialize()
        {
            // Setup in-memory database with a unique name for this test run
            var dbName = Guid.NewGuid().ToString();
            _options = new DbContextOptionsBuilder<SurveyDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            // Create an initial context for setting up test data
            _dbContext = new SurveyDbContext(_options);
            
            // Setup mock context factory
            _mockContextFactory = new Mock<IDbContextFactory<SurveyDbContext>>();
            _mockContextFactory.Setup(cf => cf.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new SurveyDbContext(_options));
            
            // Setup test logger
            _loggerFactory = new TestContextLoggerFactory(TestContext);
            _logger = _loggerFactory.CreateLogger<LogService>();
            
            // Create LogService
            _logService = new LogService(_mockContextFactory.Object, _logger);

            // Setup test service with proper logger
            _testSlackService = new TestSlackService(_logService);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _dbContext.Dispose();
            TestContextLogger.ClearLogsForTest(TestContext.TestName);
        }

        [TestMethod]
        public async Task VerifyParticipants_CapturesCorrectParameters()
        {
            // Arrange
            var participants = new List<string> { "user1@example.com", "user2@example.com" };
            var expectedReturn = new List<string> { "user1@example.com" }; // Simulate one invalid email
            _testSlackService.VerifyParticipantsReturnValue = expectedReturn;

            // Act
            var result = await _testSlackService.VerifyParticipants(participants);

            // Assert
            Assert.AreEqual(1, _testSlackService.VerifiedParticipants.Count, "Method should be called once");
            CollectionAssert.AreEqual(participants, _testSlackService.VerifiedParticipants[0], "Parameters should match");
            CollectionAssert.AreEqual(expectedReturn, result, "Return value should match expected");
        }

        [TestMethod]
        public async Task SendSurveyInvitation_CapturesCorrectParameters()
        {
            // Arrange
            var email = "test@example.com";
            var surveyId = Guid.NewGuid();
            var surveyTitle = "Test Survey";
            var surveyManager = "Test Manager";
            var entryCode = "ABC123";
            _testSlackService.InvitationReturnValue = true;

            // Act
            var result = await _testSlackService.SendSurveyInvitation(email, surveyId, surveyTitle, surveyManager, entryCode);

            // Assert
            Assert.AreEqual(1, _testSlackService.SentInvitations.Count, "Method should be called once");
            var invocation = _testSlackService.SentInvitations[0];
            Assert.AreEqual(email, invocation.Email, "Email parameter should match");
            Assert.AreEqual(surveyId, invocation.SurveyId, "SurveyId parameter should match");
            Assert.AreEqual(surveyTitle, invocation.Title, "Survey title parameter should match");
            Assert.AreEqual(surveyManager, invocation.Manager, "Survey manager parameter should match");
            Assert.AreEqual(entryCode, invocation.EntryCode, "Entry code parameter should match");
            Assert.IsTrue(result, "Return value should match expected");
        }

        [TestMethod]
        public async Task SendBulkMessages_CapturesCorrectParameters()
        {
            // Arrange
            var emails = new List<string> { "user1@example.com", "user2@example.com" };
            var message = "Test bulk message";
            var context = "test_context";
            _testSlackService.BulkMessageReturnValue = 2; // Simulate two successful sends

            // Act
            var result = await _testSlackService.SendBulkMessages(emails, message, context);

            // Assert
            Assert.AreEqual(1, _testSlackService.SentBulkMessages.Count, "Method should be called once");
            var invocation = _testSlackService.SentBulkMessages[0];
            CollectionAssert.AreEqual(emails, invocation.Recipients, "Emails parameter should match");
            Assert.AreEqual(message, invocation.Message, "Message parameter should match");
            Assert.AreEqual(context, invocation.Context, "Context parameter should match");
            Assert.AreEqual(2, result, "Return value should match expected");
        }

        [TestMethod]
        public async Task Notify_CapturesCorrectParameters()
        {
            // Arrange
            var message = "Test notification";
            var roles = new[] { Roles.Admin, Roles.SurveyManager };

            // Act
            await _testSlackService.Notify(message, roles);

            // Assert
            Assert.AreEqual(1, _testSlackService.SentNotifications.Count, "Method should be called once");
            var invocation = _testSlackService.SentNotifications[0];
            Assert.AreEqual(message, invocation.Message, "Message parameter should match");
            CollectionAssert.AreEqual(roles, invocation.Roles, "Roles parameter should match");
        }

        [TestMethod]
        public void Reset_ClearsAllTrackingCollections()
        {
            // Arrange
            // Add some test data to all tracking collections
            _testSlackService.VerifiedParticipants.Add(new List<string> { "test@example.com" });
            _testSlackService.SentInvitations.Add(("test@example.com", Guid.NewGuid(), "Test", "Manager", "123"));
            _testSlackService.SentBulkMessages.Add((new List<string> { "test@example.com" }, "Test", "Context"));
            _testSlackService.SentNotifications.Add(("Test", new[] { Roles.Admin }));
            _testSlackService.BulkMessageReturnValue = 5;
            _testSlackService.InvitationReturnValue = false;
            _testSlackService.VerifyParticipantsReturnValue = new List<string> { "invalid@example.com" };

            // Act
            _testSlackService.Reset();

            // Assert
            Assert.AreEqual(0, _testSlackService.VerifiedParticipants.Count, "VerifiedParticipants should be empty");
            Assert.AreEqual(0, _testSlackService.SentInvitations.Count, "SentInvitations should be empty");
            Assert.AreEqual(0, _testSlackService.SentBulkMessages.Count, "SentBulkMessages should be empty");
            Assert.AreEqual(0, _testSlackService.SentNotifications.Count, "SentNotifications should be empty");
            Assert.AreEqual(0, _testSlackService.BulkMessageReturnValue, "BulkMessageReturnValue should be reset");
            Assert.IsTrue(_testSlackService.InvitationReturnValue, "InvitationReturnValue should be reset");
            Assert.AreEqual(0, _testSlackService.VerifyParticipantsReturnValue.Count, "VerifyParticipantsReturnValue should be reset");
        }
    }
}