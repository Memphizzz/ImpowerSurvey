using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class SurveyCodeServiceTests
    {
        private SurveyCodeService _surveyCodeService;
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
                // Configure to ignore transaction warnings since in-memory DB doesn't support them
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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
            
            // Create a real LogService with the test logger
            _logService = new LogService(_mockContextFactory.Object, _logger);
            
            // Setup service to test
            _surveyCodeService = new SurveyCodeService(_mockContextFactory.Object, _logService);
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
        public async Task ValidateEntryCodeAsync_ValidCode_ReturnsSuccess()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Running
            };
            
            var entryCode = EntryCode.Create(surveyId);
            entryCode.Code = "VALID123";
            entryCode.IsIssued = true;
            entryCode.IsUsed = false;

            _dbContext.Surveys.Add(survey);
            _dbContext.EntryCodes.Add(entryCode);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.ValidateEntryCodeAsync("VALID123");

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(surveyId, result.Data);
            Assert.AreEqual(Constants.EntryCodes.Valid, result.Message);
        }

        [TestMethod]
        public async Task ValidateEntryCodeAsync_UsedCode_ReturnsFailure()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Running
            };
            
            var entryCode = EntryCode.Create(surveyId);
            entryCode.Code = "USED123";
            entryCode.IsIssued = true;
            entryCode.IsUsed = true; // Code is already used
            
            _dbContext.Surveys.Add(survey);
            _dbContext.EntryCodes.Add(entryCode);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.ValidateEntryCodeAsync("USED123");

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.EntryCodes.InvalidOrUsed, result.Message);
        }

        [TestMethod]
        public async Task ValidateEntryCodeAsync_InvalidCode_ReturnsFailure()
        {
            // Arrange
            // No entry code in database
            
            // Act
            var result = await _surveyCodeService.ValidateEntryCodeAsync("NONEXISTENT");

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.EntryCodes.InvalidOrUsed, result.Message);
        }

        [TestMethod]
        public async Task ValidateEntryCodeAsync_ClosedSurvey_ReturnsFailure()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Closed // Survey is closed
            };
            
            var entryCode = EntryCode.Create(surveyId);
            entryCode.Code = "CLOSED123";
            entryCode.IsIssued = true;
            entryCode.IsUsed = false;
            
            _dbContext.Surveys.Add(survey);
            _dbContext.EntryCodes.Add(entryCode);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.ValidateEntryCodeAsync("CLOSED123");

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.Survey.NoEntry, result.Message);
        }

        [TestMethod]
        public async Task BurnEntryCodeAsync_ValidCode_ReturnsTrue()
        {
            // Arrange - create a test survey context
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Running
            };
            
            // Create entry code using factory method
            var entryCode = EntryCode.Create(surveyId);
            entryCode.Code = "VALID456";
            entryCode.IsIssued = true;  // Important - SurveyCodeService requires IsIssued=true
            entryCode.IsUsed = false;
            
            _dbContext.Surveys.Add(survey);
            _dbContext.EntryCodes.Add(entryCode);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.BurnEntryCodeAsync("VALID456");

            // Assert
            Assert.IsTrue(result);
            
            // Create a new DbContext instance to verify changes were saved
			await using var verificationContext = new SurveyDbContext(_options);
            var updatedCode = await verificationContext.EntryCodes.FirstOrDefaultAsync(ec => ec.Code == "VALID456");
            Assert.IsNotNull(updatedCode);
            Assert.IsTrue(updatedCode.IsUsed);
        }

        [TestMethod]
        public async Task BurnEntryCodeAsync_DemoCode_ReturnsTrueButNotBurned()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var entryCode = EntryCode.Create(surveyId);
            entryCode.Code = "DEMO";
            entryCode.IsIssued = true;
            entryCode.IsUsed = false;
            
            _dbContext.EntryCodes.Add(entryCode);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.BurnEntryCodeAsync("DEMO");

            // Assert
            Assert.IsTrue(result);
            
            // Verify code is NOT marked as used in database (special case for "DEMO")
            var updatedCode = await _dbContext.EntryCodes.FirstOrDefaultAsync(ec => ec.Code == "DEMO");
            Assert.IsNotNull(updatedCode);
            Assert.IsFalse(updatedCode.IsUsed);
        }

        [TestMethod]
        public async Task BurnEntryCodeAsync_InvalidCode_ReturnsFalse()
        {
            // Arrange
            // No entry code in database
            
            // Act
            var result = await _surveyCodeService.BurnEntryCodeAsync("NONEXISTENT");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task GetCompletionCodeAsync_AvailableCode_ReturnsUnusedCode()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var completionCodes = new List<CompletionCode>
            {
                CompletionCode.Create(surveyId),
                CompletionCode.Create(surveyId)
            };
            
            completionCodes[0].Code = "COMP123";
            completionCodes[1].Code = "COMP456";
            
            _dbContext.CompletionCodes.AddRange(completionCodes);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.GetCompletionCodeAsync(surveyId);

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.IsTrue(result.Data == "COMP123" || result.Data == "COMP456");
        }

        [TestMethod]
        public async Task GetCompletionCodeAsync_NoAvailableCodes_ReturnsFailure()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            // No completion codes for this survey
            
            // Act
            var result = await _surveyCodeService.GetCompletionCodeAsync(surveyId);

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.CompletionCodes.NoneAvailable, result.Message);
        }

        [TestMethod]
        public async Task ValidateAndBurnCompletionCodeAsync_ValidCode_ReturnsSuccess()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                ParticipationType = ParticipationTypes.Manual,
                Participants = new List<string>() // Initialize empty participants list
            };
            
            var completionCode = CompletionCode.Create(surveyId);
            completionCode.Code = "VALIDCOMP";
            completionCode.Survey = survey; // Important - set the Survey navigation property
            
            _dbContext.Surveys.Add(survey);
            _dbContext.CompletionCodes.Add(completionCode);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.ValidateAndBurnCompletionCodeAsync(surveyId, "VALIDCOMP", "testuser");

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(Constants.CompletionCodes.Burned, result.Message);
            
            // Create a new DbContext instance to verify changes were saved
			await using var verificationContext = new SurveyDbContext(_options);
            
            // Verify code is marked as used in database
            var updatedCode = await verificationContext.CompletionCodes
                .FirstOrDefaultAsync(cc => cc.Code == "VALIDCOMP");
            Assert.IsNotNull(updatedCode);
            Assert.IsTrue(updatedCode.IsUsed);
            
            // Verify participation record was created
            var participationRecord = await verificationContext.ParticipationRecords
                .FirstOrDefaultAsync(pr => pr.CompletionCodeId == completionCode.Id);
            Assert.IsNotNull(participationRecord);
            Assert.AreEqual("testuser", participationRecord.UsedBy);
            
            // Verify user was added to participants list for manual surveys
            var updatedSurvey = await verificationContext.Surveys.FindAsync(surveyId);
            Assert.IsTrue(updatedSurvey.Participants.Contains("testuser"));
        }

        [TestMethod]
        public async Task ValidateAndBurnCompletionCodeAsync_InvalidCode_ReturnsFailure()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            // No completion code in database
            
            // Act
            var result = await _surveyCodeService.ValidateAndBurnCompletionCodeAsync(surveyId, "NONEXISTENT", "testuser");

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.CompletionCodes.Invalid, result.Message);
        }

        [TestMethod]
        public async Task GetEntryCode_AvailableCode_ReturnsAndMarksIssued()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var entryCodes = new List<EntryCode>
            {
                EntryCode.Create(surveyId),
                EntryCode.Create(surveyId)
            };
            
            entryCodes[0].Code = "ENTRY123";
            entryCodes[0].IsIssued = false;
            entryCodes[0].IsUsed = false;
            
            entryCodes[1].Code = "ENTRY456";
            entryCodes[1].IsIssued = false;
            entryCodes[1].IsUsed = false;
            
            _dbContext.EntryCodes.AddRange(entryCodes);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _surveyCodeService.GetEntryCode(surveyId, true);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Code == "ENTRY123" || result.Code == "ENTRY456");
            Assert.IsTrue(result.IsIssued);
            Assert.IsFalse(result.IsUsed);
        }

        [TestMethod]
        public async Task GetEntryCode_NoAvailableCode_ReturnsNull()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            // No entry codes for this survey
            
            // Act
            var result = await _surveyCodeService.GetEntryCode(surveyId, true);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task MarkEntryCodeIssued_ValidCode_ReturnsTrue()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Running
            };
            
            var entryCode = EntryCode.Create(surveyId);
            entryCode.Code = "MARK123";
            entryCode.IsIssued = false;
            entryCode.IsUsed = false;
            
            _dbContext.Surveys.Add(survey);
            _dbContext.EntryCodes.Add(entryCode);
            await _dbContext.SaveChangesAsync();

            // Make sure to capture the entryCode.Id after it's been saved
            var entryCodeId = entryCode.Id;

            // Act
            var result = await _surveyCodeService.MarkEntryCodeIssued(entryCodeId);

            // Assert
            Assert.IsTrue(result);
            
            // Create a new DbContext instance to verify changes were saved
			await using var verificationContext = new SurveyDbContext(_options);
            
            // Verify code is marked as issued in database
            var updatedCode = await verificationContext.EntryCodes.FindAsync(entryCodeId);
            Assert.IsNotNull(updatedCode);
            Assert.IsTrue(updatedCode.IsIssued);
        }

        [TestMethod]
        public async Task MarkEntryCodeIssued_InvalidCode_ReturnsFalse()
        {
            // Arrange
            var invalidCodeId = Guid.NewGuid();
            
            // Act
            var result = await _surveyCodeService.MarkEntryCodeIssued(invalidCodeId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task TopUpEntryCodesAsync_RunningSurvey_GeneratesNewCodes()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Running
            };
            
            _dbContext.Surveys.Add(survey);
            await _dbContext.SaveChangesAsync();
            
            // Count current entry codes
            var initialCount = await _dbContext.EntryCodes.CountAsync(e => e.SurveyId == surveyId);

            // Act
            var result = await _surveyCodeService.TopUpEntryCodesAsync(surveyId);

            // Assert
            Assert.AreEqual(10, result); // Should generate 10 codes
            
            // Verify codes were added to database
            var newCount = await _dbContext.EntryCodes.CountAsync(e => e.SurveyId == surveyId);
            Assert.AreEqual(initialCount + 10, newCount);
            
            // Verify logging occurred by checking the logs from TestContextLogger
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Information") && log.Contains("Generated 10 additional entry codes")));
        }

        [TestMethod]
        public async Task TopUpEntryCodesAsync_ClosedSurvey_GeneratesNoCodes()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Test Survey",
                State = SurveyStates.Closed // Closed survey
            };
            
            _dbContext.Surveys.Add(survey);
            await _dbContext.SaveChangesAsync();
            
            // Count current entry codes
            var initialCount = await _dbContext.EntryCodes.CountAsync(e => e.SurveyId == surveyId);

            // Act
            var result = await _surveyCodeService.TopUpEntryCodesAsync(surveyId);

            // Assert
            Assert.AreEqual(0, result); // Should not generate any codes
            
            // Verify no codes were added to database
            var newCount = await _dbContext.EntryCodes.CountAsync(e => e.SurveyId == surveyId);
            Assert.AreEqual(initialCount, newCount);
            
            // Verify warning was logged using TestContextLogger
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Warning") && log.Contains("Attempted to top up entry codes for invalid survey")));
        }
    }
}