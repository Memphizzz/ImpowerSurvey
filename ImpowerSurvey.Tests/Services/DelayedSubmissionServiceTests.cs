using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class DelayedSubmissionServiceTests
    {
        private DelayedSubmissionService _delayedSubmissionService;
        private SurveyDbContext _dbContext;
        private ILogService _logService;
        private ILogger<LogService> _logger;
        private DbContextOptions<SurveyDbContext> _options;
        private Mock<IDbContextFactory<SurveyDbContext>> _mockContextFactory;
        private DssConfiguration _dssConfig;
        private TestContextLoggerFactory _loggerFactory;
        private Mock<ILeaderElectionService> _mockLeaderElectionService;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public async Task TestInitialize()
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
            
            // Configure DSS with reasonable test values
            _dssConfig = new DssConfiguration
            {
                MinPercentage = 10,
                MaxPercentage = 50,
                PercentageIncrement = 5,
                ResetChancePercentage = 20,
                MinimumSurveySubmissions = 3
            };
            
            // Initialize leader election settings
            await InitializeLeaderElectionSettingsAsync();
            
            // Create a mock LeaderElectionService
            _mockLeaderElectionService = new Mock<ILeaderElectionService>();
            _mockLeaderElectionService.Setup(les => les.IsLeader).Returns(true);
            _mockLeaderElectionService.Setup(les => les.InstanceId).Returns("test-instance-id");

            
            // Create the service with mock dependencies
            var mockClaudeService = new Mock<IClaudeService>();
            mockClaudeService.Setup(cs => cs.AnonymizeTextAsync(It.IsAny<string>())).Returns<string>(Task.FromResult);
                
            _delayedSubmissionService = new DelayedSubmissionService(_mockContextFactory.Object, _dssConfig, _logService, _mockLeaderElectionService.Object, mockClaudeService.Object, null, null, null);
        }
        
        /// <summary>
        /// Initialize the leader election settings in the database
        /// </summary>
        private async Task InitializeLeaderElectionSettingsAsync()
        {
            // Add default leader election settings
            _dbContext.Settings.Add(new Setting 
            { 
                Id = Guid.NewGuid(),
                Key = Constants.SettingsKeys.LeaderId, 
                Value = "test-instance-id",
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
            
            await _dbContext.SaveChangesAsync();
        }
        
        [TestCleanup]
        public void TestCleanup()
        {
            // Dispose the initial context used for setting up test data
            _dbContext.Dispose();
            
            // Clean up the service
            _delayedSubmissionService.Dispose();
            
            // Clear test logs for this test
            TestContextLogger.ClearLogsForTest(TestContext.TestName);
        }

        [TestMethod]
        public async Task FlushPendingResponses_EmptyQueue_ReturnsZero()
        {
            // Arrange
            var survey = new Survey { Title = "Test Survey", State = SurveyStates.Running };
            _dbContext.Surveys.Add(survey);
            await _dbContext.SaveChangesAsync();
            
            // Act
            var result =  await _delayedSubmissionService.FlushPendingResponses(survey.Id);
            
            // Assert
            Assert.AreEqual(0, result.Data, "Should return 0 when there are no pending responses");
        }
        
        [DataTestMethod]
        [DataRow("Test Survey", "Test Question", "Test Answer 1", "Test Answer 2")]
        public async Task QueueResponses_ThenFlushPendingResponses_ProcessesAllResponses(string surveyTitle, string questionText, string answer1, string answer2)
        {
            // Arrange
            // Add a survey and question to the database
            var survey = new Survey
            {
                Title = surveyTitle,
                State = SurveyStates.Running
            };
            var question = new Question
            {
                Text = questionText,
                Type = QuestionTypes.Text
            };
            survey.Questions.Add(question);
            _dbContext.Surveys.Add(survey);
            await _dbContext.SaveChangesAsync();
            
            // Create test responses
            var responses = new List<Response>
            {
                new()
                {
                    SurveyId = survey.Id,
                    QuestionId = question.Id,
                    Answer = answer1,
                    QuestionType = QuestionTypes.Text
                },
                new()
                {
                    SurveyId = survey.Id,
                    QuestionId = question.Id,
                    Answer = answer2,
                    QuestionType = QuestionTypes.Text
                }
            };
            
            // Act
            // Queue the responses
            _delayedSubmissionService.QueueResponses(responses);
            
            // Flush the responses
            var result = await _delayedSubmissionService.FlushPendingResponses(survey.Id);
            
            // Assert
            Assert.AreEqual(2, result.Data, "Should have flushed both responses");
            
            // Verify responses were actually saved to the database
            await using var context = new SurveyDbContext(_options);
            var savedResponses = await context.Responses
                                              .Where(r => r.SurveyId == survey.Id)
                                              .ToListAsync();
                
            Assert.AreEqual(2, savedResponses.Count, "Should have saved both responses to the database");
        }

        [TestMethod]
        public async Task QueueResponses_ThenFlushDifferentSurvey_OnlyFlushesMatchingSurvey()
        {
            // Arrange
            // Add surveys with questions to the database
            var survey1 = new Survey 
            {
                Title = "Survey 1", 
                State = SurveyStates.Running 
            };
            
            var survey2 = new Survey 
            { 
                Title = "Survey 2", 
                State = SurveyStates.Running 
            };
            
            // Create questions for each survey
            var question1 = new Question
            {
                Text = "Question for Survey 1",
                Type = QuestionTypes.Text
            };
            
            var question2 = new Question
            {
                Text = "Question for Survey 2",
                Type = QuestionTypes.Text
            };
            
            // Add the questions to their surveys
            survey1.Questions.Add(question1);
            survey2.Questions.Add(question2);
            
            // Add to database and save to get IDs
            _dbContext.Surveys.AddRange(survey1, survey2);
            await _dbContext.SaveChangesAsync();
            
            // Create test responses for both surveys using the saved question IDs
            var responses = new List<Response>
            {
                new()
                {
                    SurveyId = survey1.Id,
                    QuestionId = question1.Id,
                    Answer = "Survey 1 Answer",
                    QuestionType = QuestionTypes.Text
                },
                new()
                {
                    SurveyId = survey2.Id,
                    QuestionId = question2.Id,
                    Answer = "Survey 2 Answer",
                    QuestionType = QuestionTypes.Text
                }
            };
            
            // Queue all responses
            _delayedSubmissionService.QueueResponses(responses);
            
            // Act - flush only survey 1
            var result = await _delayedSubmissionService.FlushPendingResponses(survey1.Id);
            
            // Assert
            Assert.AreEqual(1, result.Data, "Should have flushed only the one response for survey 1");
            
            // Verify only survey 1 responses were saved
            await using var context = new SurveyDbContext(_options);
            var survey1Responses = await context.Responses
                                                .Where(r => r.SurveyId == survey1.Id)
                                                .ToListAsync();
            var survey2Responses = await context.Responses
                                                .Where(r => r.SurveyId == survey2.Id)
                                                .ToListAsync();
                
            Assert.AreEqual(1, survey1Responses.Count, "Should have saved 1 response for survey 1");
            Assert.AreEqual(0, survey2Responses.Count, "Should not have saved responses for survey 2");
        }

        [TestMethod]
        public async Task GetStatus_ReflectsQueueState()
        {
            // Arrange
            var surveyId = Guid.NewGuid();
            
            // Create and add survey with question
            var survey = new Survey
            {
                Id = surveyId,
                Title = "Status Test Survey",
                State = SurveyStates.Running
            };
            
            var question = new Question
            {
                Text = "Status Test Question",
                Type = QuestionTypes.Text
            };
            
            survey.Questions.Add(question);
            _dbContext.Surveys.Add(survey);
            await _dbContext.SaveChangesAsync();
            
            // Create test response with valid question ID
            var responses = new List<Response>
            {
                new()
                {
                    SurveyId = surveyId,
                    QuestionId = question.Id, // Use the saved question ID
                    Answer = "Test Answer",
                    QuestionType = QuestionTypes.Text
                }
            };
            
            // Act
            // Get status before adding any responses
            var emptyStatus = _delayedSubmissionService.GetStatus();
            
            // Queue responses
            _delayedSubmissionService.QueueResponses(responses);
            
            // Get status with responses
            var status = _delayedSubmissionService.GetStatus();
            
            // Assert
            Assert.AreEqual(0, emptyStatus.Pending, "Should start with 0 pending responses");
            Assert.AreEqual(1, status.Pending, "Should have 1 pending response after queueing");
            Assert.AreEqual(true, status.IsLeader, "Should reflect the mock's leadership status");
            Assert.AreEqual("test-instance-id", status.InstanceId, "Should reflect the mock's instance ID");
        }
        
        [TestMethod]
        public async Task FlushPendingResponses_NonLeader_ReturnsZero()
        {
            // Arrange
            // Setup a non-leader service
            _mockLeaderElectionService.Setup(les => les.IsLeader).Returns(false);
            
            var survey = new Survey { Title = "Test Survey", State = SurveyStates.Running };
            _dbContext.Surveys.Add(survey);
            await _dbContext.SaveChangesAsync();
            
            var question = new Question { Text = "Test Question", Type = QuestionTypes.Text };
            survey.Questions.Add(question);
            await _dbContext.SaveChangesAsync();
            
            // Create and queue responses
            var responses = new List<Response>
            {
                new()
                {
                    SurveyId = survey.Id,
                    QuestionId = question.Id,
                    Answer = "Test Answer",
                    QuestionType = QuestionTypes.Text
                }
            };
            
            _delayedSubmissionService.QueueResponses(responses);
            
            // Act
            var result = await _delayedSubmissionService.FlushPendingResponses(survey.Id);
            
            // Assert
            Assert.AreEqual(0, result.Data, "Non-leader should return 0 and not flush responses");
            
            // Verify no responses were saved
            await using var context = new SurveyDbContext(_options);
            var savedResponses = await context.Responses.Where(r => r.SurveyId == survey.Id).ToListAsync();
            Assert.AreEqual(0, savedResponses.Count, "Non-leader should not save any responses");
        }
    }
}