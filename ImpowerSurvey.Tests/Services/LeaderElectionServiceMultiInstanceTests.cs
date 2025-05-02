using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    [TestCategory("MultiInstanceMode")]
    public class LeaderElectionServiceMultiInstanceTests : LeaderElectionServiceTestBase
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Verify the class is running in multi-instance mode
            var isScaleOut = Environment.GetEnvironmentVariable("IS_SCALE_OUT");
            if (!bool.TryParse(isScaleOut, out var b) || !b)
                Assert.Inconclusive("These tests require IS_SCALE_OUT=true to run properly. Use the ImpowerSurvey.MultiInstanceMode.runsettings file.");
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
        public async Task StartAsync_ShouldBeAbleToAcquireLeadershipWhenNoLeaderExists()
        {
            // Arrange
            var eventFired = false;
            var eventValue = false;
            
            _leaderElectionService.OnLeadershipChanged += isLeader => {
                eventFired = true;
                eventValue = isLeader;
            };
            
            // Act
            await _leaderElectionService.StartAsync(CancellationToken.None);
            
            // Give the timer a chance to execute
            await Task.Delay(250);
            
            // Assert
            Assert.IsTrue(_leaderElectionService.IsLeader, "Service should become leader when none exists");
            Assert.IsTrue(eventFired, "Leadership change event should be fired");
            Assert.IsTrue(eventValue, "Leadership change event should indicate becoming leader");
            
            // Clean up
            await _leaderElectionService.StopAsync(CancellationToken.None);
        }
        
        [TestMethod]
        public async Task StopAsync_ShouldRelinquishLeadershipWhenLeader()
        {
            // Arrange
            // Make this instance the leader
            await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderId, LeaderElectionService.InstanceId);
            await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderHeartbeat, DateTime.UtcNow.ToString("o"));
            
            var eventFired = false;
            var eventValue = false;
            
            _leaderElectionService.OnLeadershipChanged += isLeader => {
                eventFired = true;
                eventValue = isLeader;
            };
            
            // Start the service
            await _leaderElectionService.StartAsync(CancellationToken.None);
            
            // Give the timer a chance to execute
            await Task.Delay(250);
            
            // Verify we're the leader
            Assert.IsTrue(_leaderElectionService.IsLeader, "Service should confirm it's the leader");
            
            // Act
            await _leaderElectionService.StopAsync(CancellationToken.None);
            
            // Assert
            Assert.IsTrue(eventFired, "Leadership change event should be fired");
            Assert.IsFalse(eventValue, "Leadership change event should indicate losing leadership");
            
            // Verify the leader ID was cleared in the database
            var leaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
            Assert.AreEqual(string.Empty, leaderId, "Leader ID should be cleared in database");
        }
        
        [TestMethod]
        public async Task CheckLeadership_ShouldNotBecomeLeaderWhenAnotherActive()
        {
            // Arrange
            // Set another instance as the leader
            var otherInstanceId = "another-instance-id";
            await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderId, otherInstanceId);
            await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderHeartbeat, DateTime.UtcNow.ToString("o"));
            
            // Act
            await _leaderElectionService.StartAsync(CancellationToken.None);
            
            // Give the timer a chance to execute
            await Task.Delay(250);
            
            // Assert
            Assert.IsFalse(_leaderElectionService.IsLeader, "Service should not become leader when another is active");
            
            // Verify the leader ID is still the other instance
            var leaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
            Assert.AreEqual(otherInstanceId, leaderId, "Leader ID should still be the other instance");
            
            // Clean up
            await _leaderElectionService.StopAsync(CancellationToken.None);
        }
        
        [TestMethod]
        public async Task CheckLeadership_ShouldTakeOverWhenLeaderExpired()
        {
            // Arrange
            // Set another instance as the leader with an expired heartbeat
            var otherInstanceId = "expired-instance-id";
            await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderId, otherInstanceId);
            await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderHeartbeat, 
                DateTime.UtcNow.AddMinutes(-5).ToString("o")); // Expired heartbeat (beyond the 2 minute timeout)
            
            var becameLeader = false;
            _leaderElectionService.OnLeadershipChanged += isLeader => {
                if (isLeader) becameLeader = true;
            };
            
            // Act
            await _leaderElectionService.StartAsync(CancellationToken.None);
            
            // Give the timer a chance to execute
            await Task.Delay(250);
            
            // Assert
            Assert.IsTrue(becameLeader, "Service should become leader when previous leader expired");
            Assert.IsTrue(_leaderElectionService.IsLeader, "IsLeader should be true");
            
            // Verify the leader ID was updated in the database
            var leaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
            Assert.AreEqual(LeaderElectionService.InstanceId, leaderId, "Leader ID should be updated in database");
            
            // Clean up
            await _leaderElectionService.StopAsync(CancellationToken.None);
        }
    }
}