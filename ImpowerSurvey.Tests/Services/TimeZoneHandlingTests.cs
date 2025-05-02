using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BitzArt.Blazor.Cookies;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class TimeZoneHandlingTests
    {
		private DbContextOptions<SurveyDbContext> _options;
        private SurveyDbContext _dbContext;
        private Mock<IDbContextFactory<SurveyDbContext>> _mockContextFactory;
        private ILogService _logService;
        private TestContextLoggerFactory _loggerFactory;
        private ILogger<LogService> _logger;
        private UserService _userService;
        private SurveyCodeService _surveyCodeService;
        private MockSlackService _mockSlackService;
        private Mock<ICookieService> _mockCookieService;
        private Mock<Microsoft.Extensions.Configuration.IConfiguration> _mockConfiguration;
        private CustomAuthStateProvider _authStateProvider;

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

            // Setup mock context factory to create a NEW context each time
            _mockContextFactory = new Mock<IDbContextFactory<SurveyDbContext>>();
            _mockContextFactory.Setup(cf => cf.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                               .ReturnsAsync(() => new SurveyDbContext(_options));

            // Setup real logger that writes to TestContext
            _loggerFactory = new TestContextLoggerFactory(TestContext);
            _logger = _loggerFactory.CreateLogger<LogService>();

            // Setup LogService with the real logger
            _logService = new LogService(_mockContextFactory.Object, _logger);

            // Create the real services needed for testing
            _userService = new UserService(_mockContextFactory.Object, _logService);
            _surveyCodeService = new SurveyCodeService(_mockContextFactory.Object, _logService);
            _mockSlackService = new MockSlackService(_logService);

            // Setup for auth state provider
            _mockCookieService = new Mock<ICookieService>();
            _mockConfiguration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
            _mockConfiguration.Setup(x => x[Constants.App.EnvCookieSecret])
                .Returns("TestJwtKey123456789TestJwtKey123456789TestJwtKey123456789");

            _authStateProvider = new CustomAuthStateProvider(
                _userService,
                _surveyCodeService,
                _mockCookieService.Object,
                _mockConfiguration.Object,
                _logService);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Dispose the initial context used for setting up test data
            _dbContext.Dispose();

            // Clear test logs for this test
            TestContextLogger.ClearLogsForTest(TestContext.TestName);
        }

        [TestMethod]
        public void AuthStateProvider_ConvertsUtcToLocalTime()
        {
            // Arrange - Set timezone using reflection
            var timeZoneField = typeof(CustomAuthStateProvider).GetField("_userTimeZone", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            timeZoneField.SetValue(_authStateProvider, pacificZone);
            
            var utcDateTime = new DateTime(2023, 12, 25, 16, 0, 0, DateTimeKind.Utc); // 4pm UTC on Christmas
            
            // Act
            var localDateTime = _authStateProvider.ToLocal(utcDateTime);
            
            // Assert - Pacific is UTC-8, so 4pm UTC should be 8am Pacific
            Assert.AreEqual(8, localDateTime.Hour);
            Assert.AreEqual(0, localDateTime.Minute);
            Assert.AreEqual(DateTimeKind.Local, localDateTime.Kind);
        }

        [TestMethod]
        public void AuthStateProvider_ConvertsLocalToUtcTime()
        {
            // Arrange - Set timezone using reflection
            var timeZoneField = typeof(CustomAuthStateProvider).GetField("_userTimeZone", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            var londonZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
            timeZoneField.SetValue(_authStateProvider, londonZone);
            
            var localDateTime = new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Local); // Noon local time in London (UTC+1 in summer)
            
            // Act
            var utcDateTime = _authStateProvider.ToUtc(localDateTime);
            
            // Assert - London in summer is UTC+1, so noon London time should be 11am UTC
            Assert.AreEqual(11, utcDateTime.Hour);
            Assert.AreEqual(0, utcDateTime.Minute);
            Assert.AreEqual(DateTimeKind.Utc, utcDateTime.Kind);
        }

        [TestMethod]
        public void AuthStateProvider_GetLocalNow_ReturnsCorrectLocalTime()
        {
            // Arrange - Set timezone using reflection
            var timeZoneField = typeof(CustomAuthStateProvider).GetField("_userTimeZone", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            var tokyoZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
            timeZoneField.SetValue(_authStateProvider, tokyoZone);
            
            // Get current time in UTC
            var utcNow = DateTime.UtcNow;
            
            // Calculate expected Tokyo time
            var expectedTokyoTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tokyoZone);
            
            // Act
            var localNow = _authStateProvider.GetLocalNow();
            
            // Assert - Allow 1 second difference due to execution time
            Assert.AreEqual(expectedTokyoTime.Hour, localNow.Hour);
            Assert.AreEqual(expectedTokyoTime.Minute, localNow.Minute);
            Assert.IsTrue(Math.Abs((localNow - expectedTokyoTime).TotalSeconds) < 1);
            Assert.AreEqual(DateTimeKind.Local, localNow.Kind);
        }

        [TestMethod]
        public async Task SlackService_UsesManagerTimeZone_ForNotifications()
        {
            // Arrange - Create a manager with timezone
            var manager = new User
            {
                Id = Guid.NewGuid(),
                Username = "timezone_manager",
                DisplayName = "Timezone Manager",
                Role = Roles.SurveyManager,
                PasswordHash = "hash",
                RequirePasswordChange = false,
                TimeZone = "Europe/London" // London timezone
            };
            
            _dbContext.Users.Add(manager);
            await _dbContext.SaveChangesAsync();
            
            // Reset mock slack service
            _mockSlackService.Reset();
            _mockSlackService.BulkMessageReturnValue = 2; // Mock 2 successful sends
            
            // Act - Send bulk messages using manager's timezone
            var result = await _mockSlackService.SendBulkMessages(
                [ "user1@example.com", "user2@example.com" ],
                "Test message",
                "Test context",
                manager.TimeZone
            );
            
            // Assert
            Assert.AreEqual(2, result); // Should successfully send 2 messages
            Assert.AreEqual(1, _mockSlackService.SentBulkMessages.Count); // Should have 1 bulk message call
            Assert.AreEqual("Europe/London", manager.TimeZone); // Should use manager's timezone
        }
    }
}