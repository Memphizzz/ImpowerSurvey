using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq.Protected;

namespace ImpowerSurvey.Tests.Services
{
    /// <summary>
    /// Base class for LeaderElectionService tests that contains common setup logic
    /// </summary>
    public abstract class LeaderElectionServiceTestBase
    {
        protected LeaderElectionService _leaderElectionService;
        protected SurveyDbContext _dbContext;
        protected ILogService _logService;
        protected ISettingsService _settingsService;
        protected Mock<IDbContextFactory<SurveyDbContext>> _mockContextFactory;
        protected Mock<IHttpClientFactory> _mockHttpClientFactory;
        protected DbContextOptions<SurveyDbContext> _options;
        protected TestContextLoggerFactory _loggerFactory;
        protected ILogger<LogService> _logger;
        
        public TestContext TestContext { get; set; }
        
        /// <summary>
        /// Common initialization for all LeaderElectionService tests
        /// </summary>
        protected virtual async Task InitializeBaseTest()
        {
            // Setup in-memory database with a unique name for this test run
            var dbName = Guid.NewGuid().ToString();
            _options = new DbContextOptionsBuilder<SurveyDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            // Create initial context for setting up test data
            _dbContext = new SurveyDbContext(_options);
            
            // Setup mock context factory
            _mockContextFactory = new Mock<IDbContextFactory<SurveyDbContext>>();
            _mockContextFactory.Setup(cf => cf.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new SurveyDbContext(_options));
            
            // Setup mock HTTP client factory
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            SetupMockHttpClientFactory();
            
            // Setup logger
            _loggerFactory = new TestContextLoggerFactory(TestContext);
            _logger = _loggerFactory.CreateLogger<LogService>();
            
            // Create real LogService
            _logService = new LogService(_mockContextFactory.Object, _logger);
            
            // Create test SettingsService
            _settingsService = new TestSettingsService(_mockContextFactory.Object, _logService);
            
            // Initialize leader election settings
            await InitializeLeaderElectionSettingsAsync();
            
            // Create the service
            _leaderElectionService = new LeaderElectionService(_settingsService, _logService, _mockHttpClientFactory.Object);
        }
        
        /// <summary>
        /// Sets up the mock HTTP client factory to return a successful verification result
        /// </summary>
        protected virtual void SetupMockHttpClientFactory()
        {
            // Create a mock HTTP message handler
            var mockHandler = new Mock<HttpMessageHandler>();
            
            // Set up the protected SendAsync method
            mockHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(
                        ServiceResult.Success(true, "Inter-instance communication verified successfully for testing")
                    ))
                });
            
            // Create a real HttpClient with the mocked handler
            var httpClient = new HttpClient(mockHandler.Object);
            
            // Set up the mock HttpClientFactory to return our mocked HttpClient
            _mockHttpClientFactory
                .Setup(x => x.CreateClient(HttpClientExtensions.InstanceHttpClientName))
                .Returns(httpClient);
        }
        
        /// <summary>
        /// Initialize the leader election settings in the database
        /// </summary>
        protected virtual async Task InitializeLeaderElectionSettingsAsync()
        {
            // Add default leader election settings
            _dbContext.Settings.Add(new Setting 
            { 
                Id = Guid.NewGuid(),
                Key = Constants.SettingsKeys.LeaderId, 
                Value = string.Empty,
                Type = SettingType.String,
                Category = "System",
                Description = "ID of the current leader instance"
            });
            
            _dbContext.Settings.Add(new Setting 
            { 
                Id = Guid.NewGuid(),
                Key = Constants.SettingsKeys.LeaderHeartbeat, 
                Value = DateTime.UtcNow.ToString("o"),
                Type = SettingType.String,
                Category = "System",
                Description = "Last heartbeat from leader"
            });
            
            _dbContext.Settings.Add(new Setting 
            { 
                Id = Guid.NewGuid(),
                Key = Constants.SettingsKeys.LeaderTimeout, 
                Value = "2",
                Type = SettingType.Number,
                Category = "System",
                Description = "Minutes before leader timeout"
            });
            
            _dbContext.Settings.Add(new Setting 
            { 
                Id = Guid.NewGuid(),
                Key = Constants.SettingsKeys.LeaderCheckIntervalSeconds, 
                Value = "10", // Use a small value for testing
                Type = SettingType.Number,
                Category = "System",
                Description = "Seconds between leader election checks"
            });
            
            await _dbContext.SaveChangesAsync();
        }
        
        /// <summary>
        /// Common cleanup logic for all tests
        /// </summary>
        protected virtual void CleanupBaseTest()
        {
            // Dispose the initial context used for setting up test data
            _dbContext?.Dispose();
            
            // Clean up the service
            _leaderElectionService?.Dispose();
            
            // Clear test logs for this test
            if (TestContext != null)
            {
                TestContextLogger.ClearLogsForTest(TestContext.TestName);
            }
        }
    }
}