using ImpowerSurvey.Services;
using BitzArt.Blazor.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class CustomAuthStateProviderTests
    {
        private Mock<UserService> _mockUserService;
        private Mock<SurveyCodeService> _mockSurveyCodeService;
        private Mock<ICookieService> _mockCookieService;
        private Mock<IConfiguration> _mockConfiguration;
        private Mock<ILogService> _mockLogService;
        private CustomAuthStateProvider _authStateProvider;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockUserService = new Mock<UserService>(It.IsAny<IDbContextFactory<SurveyDbContext>>(), It.IsAny<ILogService>());
            
            _mockSurveyCodeService = new Mock<SurveyCodeService>(It.IsAny<IDbContextFactory<SurveyDbContext>>(), _mockLogService);
            
            _mockCookieService = new Mock<ICookieService>();
            _mockConfiguration = new Mock<IConfiguration>();
            _mockLogService = new Mock<ILogService>();

            // Set up configuration to return a test JWT key
            _mockConfiguration.Setup(x => x[Constants.App.EnvCookieSecret])
                .Returns("TestJwtKey123456789TestJwtKey123456789TestJwtKey123456789");

            _authStateProvider = new CustomAuthStateProvider(
                _mockUserService.Object,
                _mockSurveyCodeService.Object,
                _mockCookieService.Object,
                _mockConfiguration.Object,
                _mockLogService.Object);
        }

        [TestMethod]
        public void GetUserTimeZone_DefaultsToUtc()
        {
            // Act
            var timezone = _authStateProvider.GetUserTimeZone();

            // Assert
            Assert.AreEqual(TimeZoneInfo.Utc, timezone);
        }

        [TestMethod]
        public void ToLocal_ConvertsUtcToLocalTimeZone()
        {
            // Arrange
            var utcNow = DateTime.UtcNow;
            var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            var pacificNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, pacificZone);
            
            // Use reflection to set _userTimeZone field
            var field = typeof(CustomAuthStateProvider).GetField("_userTimeZone", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            field.SetValue(_authStateProvider, pacificZone);

            // Act
            var result = _authStateProvider.ToLocal(utcNow);

            // Assert
            Assert.AreEqual(pacificNow.Hour, result.Hour);
            Assert.AreEqual(pacificNow.Minute, result.Minute);
            Assert.AreEqual(DateTimeKind.Local, result.Kind);
        }

        [TestMethod]
        public void ToUtc_ConvertsLocalToUtcTimeZone()
        {
            // Arrange
            var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            var localTime = new DateTime(2023, 7, 15, 10, 30, 0);
            
            // Use reflection to set _userTimeZone field
            var field = typeof(CustomAuthStateProvider).GetField("_userTimeZone", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            field.SetValue(_authStateProvider, pacificZone);
            
            // Calculate expected UTC time
            var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(localTime, pacificZone);

            // Act
            var result = _authStateProvider.ToUtc(localTime);

            // Assert
            Assert.AreEqual(expectedUtc.Hour, result.Hour);
            Assert.AreEqual(expectedUtc.Minute, result.Minute);
            Assert.AreEqual(DateTimeKind.Utc, result.Kind);
        }

        [TestMethod]
        public void GetLocalNow_ReturnsCorrectLocalTime()
        {
            // Arrange
            var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            var utcNow = DateTime.UtcNow;
            var expectedLocalNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, pacificZone);

            // Use reflection to set _userTimeZone field
            var field = typeof(CustomAuthStateProvider).GetField("_userTimeZone", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            field.SetValue(_authStateProvider, pacificZone);

            // Act
            var result = _authStateProvider.GetLocalNow();

            // Assert - allow 1 second tolerance for test execution time
            Assert.IsTrue(Math.Abs((result - expectedLocalNow).TotalSeconds) < 1);
            Assert.AreEqual(DateTimeKind.Local, result.Kind);
        }
    }
}