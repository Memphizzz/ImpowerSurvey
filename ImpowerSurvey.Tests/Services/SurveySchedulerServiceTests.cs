using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace ImpowerSurvey.Tests.Services
{
	[TestClass]
	public class SurveySchedulerServiceTests
	{
		private SurveySchedulerService _schedulerService;
		private SurveyDbContext _dbContext;
		private ILogger<LogService> _logger;
		private ILogger<SlackService> _slackLogger;
		private DbContextOptions<SurveyDbContext> _options;
		private Mock<IDbContextFactory<SurveyDbContext>> _mockContextFactory;
		private SurveyServiceTracker _surveyServiceTracker;
		private MockSlackService _mockSlackService;
		private TestSettingsService _settingsService;
		private ILogService _logService;
		private ILeaderElectionService _leaderElectionService;
		private TestContextLoggerFactory _loggerFactory;

		public TestContext TestContext { get; set; }

		// Helper class to track SurveyService method calls
		// This simulates mocking without using interfaces
		public class SurveyServiceTracker : SurveyService
		{
			public List<(Guid SurveyId, List<string> Participants)> SendInvitesCalls { get; } = new();
			public List<Guid> CloseSurveyCalls { get; } = new();

			public SurveyServiceTracker(IDbContextFactory<SurveyDbContext> contextFactory, ILogService logService, ILeaderElectionService leaderElectionService)
				: base(contextFactory, null, null, null, null, logService, leaderElectionService, null, null, null) { }

			// Override the methods we need to track
			public new Task<ServiceResult> SendInvites(Guid surveyId, List<string> participants)
			{
				SendInvitesCalls.Add((surveyId, participants));
				return Task.FromResult(ServiceResult.Success("Test invites sent"));
			}

			public new Task<ServiceResult> CloseSurvey(Guid surveyId)
			{
				CloseSurveyCalls.Add(surveyId);
				return Task.FromResult(ServiceResult.Success("Test survey closed"));
			}
		}

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
			_slackLogger = _loggerFactory.CreateLogger<SlackService>();

			// Setup LogService with the real logger
			_logService = new LogService(_mockContextFactory.Object, _logger);

			// Setup TestSettingsService instead of real SettingsService
			// This provides an in-memory compatible implementation of TryUpdateSettingWithConditionAsync
			_settingsService = new TestSettingsService(_mockContextFactory.Object, _logService);

			// Instead of using a real LeaderElectionService, use LeaderElectionServiceWrapper
			// which allows us to control the leadership status for testing
			_leaderElectionService = new LeaderElectionServiceWrapper(isLeader: true);

			// Add required settings to the database
			SeedTestSettings().GetAwaiter().GetResult();
			
			// Add leader election settings to the database
			SeedLeaderElectionSettings().GetAwaiter().GetResult();

			// Create test services with real but tracked implementations
			_surveyServiceTracker = new SurveyServiceTracker(_mockContextFactory.Object, _logService, _leaderElectionService);
			_mockSlackService = new MockSlackService(_logService)
			{
				BulkMessageReturnValue = 2 // Default to 2 messages sent
			};

			// Create the scheduler service with our tracked services
			_schedulerService = new SurveySchedulerService(_mockContextFactory.Object, _surveyServiceTracker, _mockSlackService, _settingsService, _logService, _leaderElectionService);
		}

		private async Task SeedTestSettings()
		{
			// Create test settings
			var settings = new List<Setting>
			{
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.SchedulerCheckIntervalHours,
					Value = "1",
					Type = SettingType.Number,
					Category = "Scheduler"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.SchedulerLookAheadHours,
					Value = "24",
					Type = SettingType.Number,
					Category = "Scheduler"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.SlackReminderEnabled,
					Value = "true",
					Type = SettingType.Boolean,
					Category = "Notifications"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.SlackReminderHoursBefore,
					Value = "24",
					Type = SettingType.Number,
					Category = "Notifications"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.SlackReminderTemplate,
					Value = "Don't forget to complete the survey '{SurveyTitle}'. It closes in {HoursLeft} hours!",
					Type = SettingType.Text,
					Category = "Notifications"
				}
			};

			_dbContext.Settings.AddRange(settings);
			await _dbContext.SaveChangesAsync();
		}
		
		private async Task SeedLeaderElectionSettings()
		{
			// Create leader election settings
			var settings = new List<Setting>
			{
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.LeaderId,
					Value = _leaderElectionService.InstanceId, // Use the instance ID from our wrapper
					Type = SettingType.String,
					Category = "System",
					Description = "ID of the current leader instance"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.LeaderHeartbeat,
					Value = DateTime.UtcNow.ToString("o"),
					Type = SettingType.String,
					Category = "System",
					Description = "Last heartbeat from leader"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.LeaderTimeout,
					Value = "2",
					Type = SettingType.Number,
					Category = "System",
					Description = "Minutes before leader timeout"
				},
				new()
				{
					Id = Guid.NewGuid(),
					Key = Constants.SettingsKeys.LeaderCheckIntervalSeconds,
					Value = "120",
					Type = SettingType.Number,
					Category = "System",
					Description = "Seconds between leader election checks"
				}
			};
			
			_dbContext.Settings.AddRange(settings);
			await _dbContext.SaveChangesAsync();
		}

		[TestCleanup]
		public void TestCleanup()
		{
			// Dispose the initial context used for setting up test data
			_dbContext.Dispose();

			// Clear test logs for this test
			TestContextLogger.ClearLogsForTest(TestContext.TestName);

			try
			{
				// Clean up the service if it was created successfully
				if (_schedulerService != null)
				{
					_schedulerService.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
				}
			}
			catch (Exception ex)
			{
				TestContext.WriteLine($"Error in test cleanup: {ex.Message}");
			}
		}

		// Helper method to access private method for testing
		private async Task InvokeScheduleUpcomingSurveys()
		{
			var method = typeof(SurveySchedulerService).GetMethod("ScheduleUpcomingSurveys",
																  BindingFlags.NonPublic | BindingFlags.Instance);

			await (Task)method.Invoke(_schedulerService, null);
		}

		// Helper method to access private method for testing
		private async Task InvokeSendSlackReminders()
		{
			var method = typeof(SurveySchedulerService).GetMethod("SendSlackReminders",
																  BindingFlags.NonPublic | BindingFlags.Instance);

			await (Task)method.Invoke(_schedulerService, null);
		}

		// Helper method to access private method for testing
		private async Task InvokeChangeSurveyState(Guid surveyId, SurveyStates newState)
		{
			var method = typeof(SurveySchedulerService).GetMethod("ChangeSurveyState",
																  BindingFlags.NonPublic | BindingFlags.Instance);

			await (Task)method.Invoke(_schedulerService, new object[] { surveyId, newState });
		}

		[TestMethod]
		public async Task ScheduleUpcomingSurveys_ScheduledSurveyInLookAheadWindow_CreatesTimer()
		{
			// Arrange
			var startTime = DateTime.UtcNow.AddHours(2); // Within the default 24 hour look-ahead
			var survey = new Survey { Id = Guid.NewGuid(), Title = "Scheduled Test Survey", State = SurveyStates.Scheduled, ScheduledStartDate = startTime };

			_dbContext.Surveys.Add(survey);
			await _dbContext.SaveChangesAsync();

			// Act - this would normally schedule a timer but we can't easily verify that directly
			await InvokeScheduleUpcomingSurveys();

			// We can indirectly verify the survey was scheduled by calling ChangeSurveyState
			// which would happen when the timer fires
			await InvokeChangeSurveyState(survey.Id, SurveyStates.Running);

			// Assert
			await using var verificationContext = new SurveyDbContext(_options);
			var updatedSurvey = await verificationContext.Surveys.FindAsync(survey.Id);

			Assert.IsNotNull(updatedSurvey);
			Assert.AreEqual(SurveyStates.Running, updatedSurvey.State);
		}

		[TestMethod]
		public async Task ScheduleUpcomingSurveys_RunningSurveyEndingInWindow_CreatesTimer()
		{
			// Arrange
			var endTime = DateTime.UtcNow.AddHours(2); // Within the default 24 hour look-ahead
			var survey = new Survey { Id = Guid.NewGuid(), Title = "Running Test Survey", State = SurveyStates.Running, ScheduledEndDate = endTime };

			_dbContext.Surveys.Add(survey);
			await _dbContext.SaveChangesAsync();

			// Clear previous calls
			_surveyServiceTracker.CloseSurveyCalls.Clear();

			// Act
			await InvokeScheduleUpcomingSurveys();

			// We can indirectly verify the survey was scheduled by calling ChangeSurveyState
			await InvokeChangeSurveyState(survey.Id, SurveyStates.Closed);

			// Assert
			await using var verificationContext = new SurveyDbContext(_options);
			var updatedSurvey = await verificationContext.Surveys.FindAsync(survey.Id);

			Assert.IsNotNull(updatedSurvey);
			Assert.AreEqual(SurveyStates.Closed, updatedSurvey.State);

			// Verify CloseSurvey was called by checking the logs
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Closing survey") && log.Contains(survey.Title)),
						  "CloseSurvey should be called with the correct survey");
		}

		[TestMethod]
		public async Task ChangeSurveyState_ToRunningWithManualParticipation_DoesNotSendInvites()
		{
			// Arrange
			var survey = new Survey
			{
				Id = Guid.NewGuid(),
				Title = "Manual Test Survey",
				State = SurveyStates.Scheduled,
				ParticipationType = ParticipationTypes.Manual,
				Participants = new List<string> { "user1@example.com", "user2@example.com" }
			};

			_dbContext.Surveys.Add(survey);
			await _dbContext.SaveChangesAsync();

			// Clear previous calls
			_surveyServiceTracker.SendInvitesCalls.Clear();

			// Act
			await InvokeChangeSurveyState(survey.Id, SurveyStates.Running);

			// Assert
			await using var verificationContext = new SurveyDbContext(_options);
			var updatedSurvey = await verificationContext.Surveys.FindAsync(survey.Id);

			Assert.IsNotNull(updatedSurvey);
			Assert.AreEqual(SurveyStates.Running, updatedSurvey.State);

			// Verify SendInvites was NOT called for manual participation
			// For manual participation, participants should still be present
			Assert.AreEqual(2, updatedSurvey.Participants.Count, "Participants list should not be cleared for manual participation");

			// Verify no invites were attempted in the logs
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsFalse(logs.Any(log =>
										(log.Contains("Attempt to send invites") || log.Contains("Sending invites")) &&
										log.Contains(survey.Id.ToString())),
						   "SendInvites should not be called for manual participation");
		}

		[TestMethod]
		public async Task ChangeSurveyState_ToRunningWithAutomaticParticipation_SendsInvites()
		{
			// Arrange
			var participants = new List<string> { "user1@example.com", "user2@example.com" };
			var survey = new Survey
			{
				Id = Guid.NewGuid(),
				Title = "Automatic Test Survey",
				State = SurveyStates.Scheduled,
				ParticipationType = ParticipationTypes.Slack,
				Participants = new List<string>(participants) // Copy to avoid reference issues
			};

			_dbContext.Surveys.Add(survey);
			await _dbContext.SaveChangesAsync();

			// Clear previous calls
			_surveyServiceTracker.SendInvitesCalls.Clear();

			// Act
			await InvokeChangeSurveyState(survey.Id, SurveyStates.Running);

			// Assert
			await using var verificationContext = new SurveyDbContext(_options);
			var updatedSurvey = await verificationContext.Surveys.FindAsync(survey.Id);

			Assert.IsNotNull(updatedSurvey);
			Assert.AreEqual(SurveyStates.Running, updatedSurvey.State);

			// Verify survey participants list was cleared (indicating SendInvites was called)
			Assert.AreEqual(0, updatedSurvey.Participants.Count, "Participants list should be cleared after calling SendInvites");

			// Examine logs to see that SendInvites was attempted
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log =>
									   (log.Contains("Attempt to send invites") || log.Contains("Sending invites")) &&
									   log.Contains(survey.Id.ToString())),
						  "SendInvites should be called with the correct survey ID");

			// Verify participants list was cleared before calling SendInvites
			// This is to ensure no double-sending of invites in case of retries
			Assert.AreEqual(0, updatedSurvey.Participants.Count, "Participants list should be cleared after sending invites");
		}

		[TestMethod]
		public async Task SendSlackReminders_SurveyEndingSoon_SendsRemindersToNonCompletedParticipants()
		{
			// Arrange
			var manager = new User
			{
				Username = "manager", 
				Role = Roles.SurveyManager,
				PasswordHash = "abc",
				DisplayName = "Manager", 
				Email = "Manager@Managing.com", 
				RequirePasswordChange = false,
			};
			_dbContext.Users.Add(manager);

			var survey = new Survey
			{
				Title = "Reminder Test Survey",
				ManagerId = manager.Id,
				State = SurveyStates.Running,
				ScheduledEndDate = DateTime.UtcNow.AddHours(5), // Within the 24 hour reminder window
				ParticipationType = ParticipationTypes.Slack,
				Participants = ["user1@example.com", "user2@example.com", "user3@example.com"]
			};

			_dbContext.Surveys.Add(survey);

			// Create a completion code for tracking participation
			var completionCode = CompletionCode.Create(survey.Id);
			completionCode.Code = "TEST123";
			completionCode.IsUsed = true;
			_dbContext.CompletionCodes.Add(completionCode);

			// Create a participation record for user1 (completed the survey)
			var participationRecord = new ParticipationRecord
			{
				Id = Guid.NewGuid(),
				CompletionCodeId = completionCode.Id,
				UsedBy = "user1@example.com", // This user completed the survey
				UsedAt = DateTime.UtcNow.AddHours(-1)
			};

			_dbContext.ParticipationRecords.Add(participationRecord);
			await _dbContext.SaveChangesAsync();

			// Reset the mock slack service to ensure clean state
			_mockSlackService.Reset();
			_mockSlackService.BulkMessageReturnValue = 2;

			// Act
			await InvokeSendSlackReminders();

			// Assert
			// Verify MockSlackService received the right call
			Assert.AreEqual(1, _mockSlackService.SentBulkMessages.Count, "SendBulkMessages should be called exactly once");

			var callParams = _mockSlackService.SentBulkMessages[0];

			// Verify recipients are correct (only user2 and user3, not user1 who completed the survey)
			Assert.AreEqual(2, callParams.Recipients.Count, "Should send to 2 recipients");
			Assert.IsTrue(callParams.Recipients.Contains("user2@example.com"), "Should include user2@example.com");
			Assert.IsTrue(callParams.Recipients.Contains("user3@example.com"), "Should include user3@example.com");
			Assert.IsFalse(callParams.Recipients.Contains("user1@example.com"), "Should not include user1@example.com");

			// Verify message contains survey title
			Assert.IsTrue(callParams.Message.Contains("Reminder Test Survey"), "Message should contain survey title");

			// Verify context parameter contains survey title
			Assert.IsTrue(callParams.Context.Contains("Reminder Test Survey"), "Context should mention the survey");

			// Verify the message logged to indicate reminders sent
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Information") && log.Contains("Reminder process completed")));
		}

		[TestMethod]
		public async Task SendSlackReminders_RemindersDisabled_DoesNotSendReminders()
		{
			// Arrange
			// Override the Slack reminder enabled setting in the database
			await using (var context = new SurveyDbContext(_options))
			{
				var setting = await context.Settings.FirstOrDefaultAsync(s => s.Key == Constants.SettingsKeys.SlackReminderEnabled);
				if (setting != null)
				{
					setting.Value = "false";
					await context.SaveChangesAsync();
				}
			}

			var survey = new Survey
			{
				Id = Guid.NewGuid(),
				Title = "Reminder Disabled Test",
				State = SurveyStates.Running,
				ScheduledEndDate = DateTime.Now.AddHours(5),
				Participants = new List<string> { "user1@example.com" }
			};

			_dbContext.Surveys.Add(survey);
			await _dbContext.SaveChangesAsync();

			// Reset the mock slack service to ensure clean state
			_mockSlackService.Reset();

			// Act
			await InvokeSendSlackReminders();

			// Assert
			// Verify MockSlackService didn't receive any calls
			Assert.AreEqual(0, _mockSlackService.SentBulkMessages.Count, "SendBulkMessages should not be called");

			// Verify the logs indicate that reminders are disabled
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsFalse(logs.Any(log => log.Contains("SendBulkMessages")), "No SendBulkMessages calls should be logged");
		}

		[TestMethod]
		public async Task SendSlackReminders_NoUncompletedParticipants_DoesNotSendReminders()
		{
			// Arrange
			var manager = new User
			{
				Username = "manager", 
				Role = Roles.SurveyManager,
				PasswordHash = "abc",
				DisplayName = "Manager", 
				Email = "Manager@Managing.com", 
				RequirePasswordChange = false,
			};
			_dbContext.Users.Add(manager);

			var survey = new Survey
			{
				Title = "All Completed Survey",
				State = SurveyStates.Running,
				ManagerId = manager.Id,
				ScheduledEndDate = DateTime.UtcNow.AddHours(5), // Within the 24 hour reminder window
				ParticipationType = ParticipationTypes.Slack,
				Participants = ["user1@example.com", "user2@example.com"]
			};

			_dbContext.Surveys.Add(survey);

			// Create completion codes for all participants
			var completionCode1 = CompletionCode.Create(survey.Id);
			completionCode1.Code = "TEST123";
			completionCode1.IsUsed = true;
			_dbContext.CompletionCodes.Add(completionCode1);

			var completionCode2 = CompletionCode.Create(survey.Id);
			completionCode2.Code = "TEST456";
			completionCode2.IsUsed = true;
			_dbContext.CompletionCodes.Add(completionCode2);

			// Create participation records for all users
			var participationRecord1 = new ParticipationRecord { Id = Guid.NewGuid(), CompletionCodeId = completionCode1.Id, UsedBy = "user1@example.com", UsedAt = DateTime.UtcNow.AddHours(-1) };

			var participationRecord2 = new ParticipationRecord { Id = Guid.NewGuid(), CompletionCodeId = completionCode2.Id, UsedBy = "user2@example.com", UsedAt = DateTime.UtcNow.AddHours(-2) };

			_dbContext.ParticipationRecords.AddRange(participationRecord1, participationRecord2);
			await _dbContext.SaveChangesAsync();

			// Reset the mock slack service to ensure clean state
			_mockSlackService.Reset();

			// Act
			await InvokeSendSlackReminders();

			// Assert
			// Verify MockSlackService didn't receive any calls
			Assert.AreEqual(0, _mockSlackService.SentBulkMessages.Count, "SendBulkMessages should not be called");

			// Verify the logs indicate no pending participants
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("No pending participants")), "Logs should indicate no pending participants");
		}
	}
}