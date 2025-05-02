using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class LogServiceTests
    {
        private ILogService _logService;
        private SurveyDbContext _dbContext;
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
            
            // Setup mock context factory to create a NEW context each time
            // This prevents disposal issues with "await using" in service methods
            _mockContextFactory = new Mock<IDbContextFactory<SurveyDbContext>>();
            _mockContextFactory.Setup(cf => cf.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new SurveyDbContext(_options));
            
            // Setup real logger that writes to TestContext
            _loggerFactory = new TestContextLoggerFactory(TestContext);
            _logger = _loggerFactory.CreateLogger<LogService>();
            
            // Setup service to test
            _logService = new LogService(_mockContextFactory.Object, _logger);
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
        public async Task LogAsync_RegularLog_LogsToDbAndILogger()
        {
            // Arrange
            var source = LogSource.UserService;
            var level = LogLevel.Information;
            var message = "Test log message";

            // Act
            await _logService.LogAsync(source, level, message);

            // Assert
            // Check if logged to ILogger
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains(source.ToString()) && log.Contains(message)));

            // Check if logged to DB
			await using var context = new SurveyDbContext(_options);
            var dbLogs = await context.Logs.ToListAsync();
            Assert.AreEqual(1, dbLogs.Count);
            Assert.AreEqual(source.ToString(), dbLogs[0].Source);
            Assert.AreEqual(level.ToString(), dbLogs[0].Level);
            Assert.AreEqual(message, dbLogs[0].Message);
        }

        [TestMethod]
        public async Task LogAsync_DelayedSubmissionServiceError_SanitizesMessage()
        {
            // Arrange
            var source = LogSource.DelayedSubmissionService;
            var level = LogLevel.Error;
            var message = "Error processing response content: Invalid format";

            // Act
            await _logService.LogAsync(source, level, message);

            // Assert
            // Check if logged to DB with sanitized message
			await using var context = new SurveyDbContext(_options);
            var dbLogs = await context.Logs.ToListAsync();
            Assert.AreEqual(1, dbLogs.Count);
            
            // Message should be sanitized as it contains "response content"
            Assert.AreEqual("Error in DelayedSubmissionService (specific details omitted for SHIELD compliance)", dbLogs[0].Message);
        }

        [TestMethod]
        public async Task LogAsync_DelayedSubmissionServiceInfo_DoesNotLogToDb()
        {
            // Arrange
            var source = LogSource.DelayedSubmissionService;
            var level = LogLevel.Information;
            var message = "Processing batch of responses";

            // Act
            await _logService.LogAsync(source, level, message);

            // Assert
            // Should log to ILogger
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains(source.ToString()) && log.Contains(message)));

            // Should NOT log to DB for non-error DSS messages
			await using var context = new SurveyDbContext(_options);
            var dbLogs = await context.Logs.ToListAsync();
            Assert.AreEqual(0, dbLogs.Count);
        }

        [TestMethod]
        public async Task LogServiceResultAsync_SuccessResult_LogsAsInfo()
        {
            // Arrange
            var source = LogSource.SurveyService;
            var result = ServiceResult.Success("Operation completed successfully");

            // Act
            await _logService.LogServiceResultAsync(result, source);

            // Assert
			await using var context = new SurveyDbContext(_options);
            var dbLogs = await context.Logs.ToListAsync();
            Assert.AreEqual(1, dbLogs.Count);
            Assert.AreEqual(LogLevel.Information.ToString(), dbLogs[0].Level);
            Assert.AreEqual(result.Message, dbLogs[0].Message);
        }

        [TestMethod]
        public async Task LogServiceResultAsync_FailureResult_LogsAsWarning()
        {
            // Arrange
            var source = LogSource.SurveyService;
            var result = ServiceResult.Failure("Operation failed");

            // Act
            await _logService.LogServiceResultAsync(result, source);

            // Assert
			await using var context = new SurveyDbContext(_options);
            var dbLogs = await context.Logs.ToListAsync();
            Assert.AreEqual(1, dbLogs.Count);
            Assert.AreEqual(LogLevel.Warning.ToString(), dbLogs[0].Level);
            Assert.AreEqual(result.Message, dbLogs[0].Message);
        }

        [TestMethod]
        public async Task LogExceptionAsync_SavesExceptionDetails()
        {
            // Arrange
            var source = LogSource.SurveyService;
            var exception = new InvalidOperationException("Test exception");
            var context = "Test operation";

            // Act
            await _logService.LogExceptionAsync(exception, source, context);

            // Assert
			await using var dbContext = new SurveyDbContext(_options);
            var dbLogs = await dbContext.Logs.ToListAsync();
            
            Assert.AreEqual(1, dbLogs.Count);
            Assert.AreEqual(LogLevel.Error.ToString(), dbLogs[0].Level);
            Assert.AreEqual($"{context}: {exception.Message}", dbLogs[0].Message);
            
            // Verify data was saved
            Assert.IsNotNull(dbLogs[0].Data);
            
            // Deserialize using JsonDocument instead of dynamic since the JSON structure may vary
            var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonDocument>(dbLogs[0].Data).RootElement;
            
            // Check that the JSON contains the expected properties
            Assert.IsTrue(jsonElement.TryGetProperty("ExceptionType", out var exceptionType), "JSON should contain ExceptionType property");
            Assert.AreEqual("InvalidOperationException", exceptionType.GetString());
            Assert.IsTrue(jsonElement.TryGetProperty("StackTrace", out _), "JSON should contain StackTrace property");
        }

        [TestMethod]
        public async Task GetLogsAsync_FiltersByDate()
        {
            // Arrange
            // Add logs with different dates
            var now = DateTime.UtcNow;
            var yesterday = now.AddDays(-1);
            var dayBefore = now.AddDays(-2);
            
            var logs = new List<Log>
            {
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Information.ToString(), Message = "Today log", Timestamp = now },
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Information.ToString(), Message = "Yesterday log", Timestamp = yesterday },
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Information.ToString(), Message = "Day before log", Timestamp = dayBefore }
            };
            
            _dbContext.Logs.AddRange(logs);
            await _dbContext.SaveChangesAsync();

            // Act - get logs from yesterday onwards
            var result = await _logService.GetLogsAsync(startDate: yesterday);

            // Assert
            Assert.AreEqual(2, result.Count); // Today and yesterday logs
            Assert.IsTrue(result.Any(l => l.Message == "Today log"));
            Assert.IsTrue(result.Any(l => l.Message == "Yesterday log"));
            Assert.IsFalse(result.Any(l => l.Message == "Day before log"));
        }

        [TestMethod]
        public async Task GetLogsAsync_FiltersByLevel()
        {
            // Arrange
            var logs = new List<Log>
            {
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Information.ToString(), Message = "Info log" },
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Warning.ToString(), Message = "Warning log" },
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Error.ToString(), Message = "Error log" }
            };
            
            _dbContext.Logs.AddRange(logs);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _logService.GetLogsAsync(level: LogLevel.Warning.ToString());

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Warning log", result[0].Message);
        }

        [TestMethod]
        public async Task GetLogsAsync_FiltersBySource()
        {
            // Arrange
            var logs = new List<Log>
            {
                new() { Source = LogSource.UserService.ToString(), Level = LogLevel.Information.ToString(), Message = "User log" },
                new() { Source = LogSource.SurveyService.ToString(), Level = LogLevel.Information.ToString(), Message = "Survey log" },
                new() { Source = LogSource.LeaderElectionService.ToString(), Level = LogLevel.Information.ToString(), Message = "Election service log" }
            };
            
            _dbContext.Logs.AddRange(logs);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _logService.GetLogsAsync(source: LogSource.SurveyService.ToString());

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Survey log", result[0].Message);
        }

        [TestMethod]
        public async Task GetLogsAsync_LimitsResultsAndOrdersByTimestampDesc()
        {
            // Arrange
            var logs = new List<Log>();
            var baseTime = DateTime.UtcNow;
            
            // Create 15 logs with decreasing timestamps
            for (var i = 0; i < 15; i++)
            {
                logs.Add(new Log
                {
                    Source = LogSource.UserService.ToString(),
                    Level = LogLevel.Information.ToString(),
                    Message = $"Log {i}",
                    Timestamp = baseTime.AddMinutes(-i) // Older as i increases
                });
            }
            
            _dbContext.Logs.AddRange(logs);
            await _dbContext.SaveChangesAsync();

            // Act - limit to 10 results
            var result = await _logService.GetLogsAsync(take: 10);

            // Assert
            Assert.AreEqual(10, result.Count);
            
            // First result should be newest (Log 0)
            Assert.AreEqual("Log 0", result[0].Message);
            
            // Last result should be 10th newest (Log 9)
            Assert.AreEqual("Log 9", result[9].Message);
        }
    }
}