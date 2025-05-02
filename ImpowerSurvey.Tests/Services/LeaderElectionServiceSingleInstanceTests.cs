namespace ImpowerSurvey.Tests.Services;

[TestClass]
[TestCategory("SingleInstanceMode")]
public class LeaderElectionServiceSingleInstanceTests : LeaderElectionServiceTestBase
{
	[ClassInitialize]
	public static void ClassInitialize(TestContext context)
	{
		// Verify the class is running in single-instance mode
		var isScaleOut = Environment.GetEnvironmentVariable("IS_SCALE_OUT");
		if (bool.TryParse(isScaleOut, out var b) && b)
			Assert.Inconclusive("These tests require IS_SCALE_OUT=false to run properly. Use the ImpowerSurvey.SingleInstanceMode.runsettings file.");
	}

	[TestInitialize]
	public async Task TestInitialize()
	{
		await InitializeBaseTest();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		CleanupBaseTest();
	}

	[TestMethod]
	public async Task StartAsync_InSingleInstanceMode_ShouldAlwaysBeLeader()
	{
		// Arrange
		var eventFired = false;
		var eventValue = false;

		_leaderElectionService.OnLeadershipChanged += isLeader =>
		{
			eventFired = true;
			eventValue = isLeader;
		};

		// Act
		await _leaderElectionService.StartAsync(CancellationToken.None);

		// Give the timer a chance to execute
		await Task.Delay(250);

		// Assert
		Assert.IsTrue(_leaderElectionService.IsLeader, "In single instance mode, service should always become leader");
		Assert.IsTrue(eventFired, "Leadership change event should be fired");
		Assert.IsTrue(eventValue, "Leadership change event should indicate becoming leader");

		// Verify logs contain single instance mode message
		var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
		var foundSingleInstanceMode = logs.Any(log => log.Contains("SingleInstanceMode"));
		Assert.IsTrue(foundSingleInstanceMode, "Logs should indicate SingleInstanceMode operation");

		// Cleanup
		await _leaderElectionService.StopAsync(CancellationToken.None);
	}

	[TestMethod]
	public async Task StopAsync_InSingleInstanceMode_ShouldNotAffectDatabase()
	{
		// Arrange
		// Save initial db state to compare later
		var initialLeaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);

		// Start service
		await _leaderElectionService.StartAsync(CancellationToken.None);

		// Give the timer a chance to execute
		await Task.Delay(250);

		// Verify we're the leader
		Assert.IsTrue(_leaderElectionService.IsLeader, "Service should be leader in single instance mode");

		// Act
		await _leaderElectionService.StopAsync(CancellationToken.None);

		// Assert
		// In single instance mode, stopping should not modify the database
		var afterLeaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
		Assert.AreEqual(initialLeaderId, afterLeaderId, "In single instance mode, stopping should not modify the leader ID");
	}

	[TestMethod]
	public async Task Behavior_InSingleInstanceMode_ShouldSkipLeaderElection()
	{
		// Act
		await _leaderElectionService.StartAsync(CancellationToken.None);

		// Give the timer a chance to execute
		await Task.Delay(250);

		await _leaderElectionService.StopAsync(CancellationToken.None);

		// Assert
		var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);

		// In single instance mode, there should be no leadership election logs
		var foundElectionProcess = logs.Any(log =>
												log.Contains("attempting to claim leadership") ||
												log.Contains("elected as the new leader") ||
												log.Contains("checking leadership"));

		Assert.IsFalse(foundElectionProcess, "No leadership election process should occur in single instance mode");
	}
}
