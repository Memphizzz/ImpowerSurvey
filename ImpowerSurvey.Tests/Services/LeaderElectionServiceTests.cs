using ImpowerSurvey.Services;
using System.Text.RegularExpressions;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class LeaderElectionServiceTests : LeaderElectionServiceTestBase
    {
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
        public void InstanceId_ShouldBeHostnameColonPort()
        {
            // Arrange
            var regex = new Regex(@"\S+:\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            
            // Assert
            Assert.IsTrue(regex.IsMatch(LeaderElectionService.InstanceId) || LeaderElectionService.InstanceId == ":", 
                "InstanceId should be a HOSTNAME:PORT");
        }
        
        [TestMethod]
        public void Interface_InstanceId_ShouldMatchStaticId()
        {
            // Arrange
            ILeaderElectionService service = _leaderElectionService;
            
            // Assert
            Assert.AreEqual(LeaderElectionService.InstanceId, service.InstanceId, 
                "Interface InstanceId should match static InstanceId");
        }
    }
}