using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    [TestClass]
    public class UserServiceTests
    {
        private UserService _userService;
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
            _userService = new UserService(_mockContextFactory.Object, _logService);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Dispose the initial context used for setting up test data
            _dbContext.Dispose();
            
            // Clear test logs for this test
            TestContextLogger.ClearLogsForTest(TestContext.TestName);
            
            // The other contexts created by the factory during test execution
            // will be automatically disposed by the "await using" in the service methods
        }

        [DataTestMethod]
        [DataRow("testuser", "Test User", "test@example.com", Roles.SurveyManager)]
        public async Task CreateUserAsync_ValidUser_ReturnsSuccess(string username, string displayName, string email, Roles role)
        {
            // Act
            var result = await _userService.CreateUserAsync(username, displayName, email, role);

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.IsFalse(string.IsNullOrEmpty(result.Data)); // Password should be returned
            
            // Verify user was created in the database
            var user = await _userService.GetUserAsync(username);
            Assert.IsNotNull(user);
            Assert.AreEqual(username, user.Username);
            Assert.AreEqual(displayName, user.DisplayName);
            Assert.AreEqual(email, user.Emails[ParticipationTypes.Manual]);
            Assert.AreEqual(role, user.Role);
            Assert.IsTrue(user.RequirePasswordChange);
        }

        [DataTestMethod]
        [DataRow("existinguser", "Existing User", "existing@example.com", Roles.SurveyManager)]
        public async Task CreateUserAsync_DuplicateUsername_ReturnsFailure(string username, string displayName, string email, Roles role)
        {
            // Arrange - Add existing user
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = "hashedpassword",
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, email } }
            };
            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.CreateUserAsync(username, "New Display Name", "new@example.com", Roles.Admin);

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.Users.Exists, result.Message);
            
            // Verify warning was logged using TestContextLogger
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Warning") && log.Contains(username)), 
                "Expected warning log containing the duplicate username");
        }

        [DataTestMethod]
        [DataRow("testuser", "Test User", "test@example.com", Roles.SurveyManager)]
        public async Task GetUserAsync_ExistingUser_ReturnsUser(string username, string displayName, string email, Roles role)
        {
            // Arrange
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = "hashedpassword",
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, email } }
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var userByUsername = await _userService.GetUserAsync(username);
            var userById = await _userService.GetUserAsync(existingUser.Id);

            // Assert
            Assert.IsNotNull(userByUsername);
            Assert.IsNotNull(userById);
            Assert.AreEqual(existingUser.Id, userByUsername.Id);
            Assert.AreEqual(existingUser.Id, userById.Id);
            Assert.AreEqual(username, userByUsername.Username);
            Assert.AreEqual(username, userById.Username);
        }

        [DataTestMethod]
        [DataRow("admin", "Admin User", Roles.Admin, "manager", "Manager User", Roles.SurveyManager, "participant", "Participant User", Roles.SurveyParticipant)]
        public async Task GetUsersByRoles_MultipleRoles_ReturnsMatchingUsers(
            string adminUsername, string adminDisplayName, Roles adminRole,
            string managerUsername, string managerDisplayName, Roles managerRole,
            string participantUsername, string participantDisplayName, Roles participantRole)
        {
            // Arrange
            var adminUser = new User
            {
                Username = adminUsername,
                DisplayName = adminDisplayName,
                PasswordHash = "hashedpassword",
                Role = adminRole
            };

            var managerUser = new User
            {
                Username = managerUsername,
                DisplayName = managerDisplayName,
                PasswordHash = "hashedpassword",
                Role = managerRole
            };

            var participantUser = new User
            {
                Username = participantUsername,
                DisplayName = participantDisplayName,
                PasswordHash = "hashedpassword",
                Role = participantRole
            };

            _dbContext.Users.AddRange(adminUser, managerUser, participantUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var adminAndManagerUsers = await _userService.GetUsersByRoles(adminRole, managerRole);
            var participantUsers = await _userService.GetUsersByRoles(participantRole);

            // Assert
            Assert.AreEqual(2, adminAndManagerUsers.Count());
            Assert.AreEqual(1, participantUsers.Count());
            Assert.IsTrue(adminAndManagerUsers.Any(u => u.Username == adminUsername));
            Assert.IsTrue(adminAndManagerUsers.Any(u => u.Username == managerUsername));
            Assert.IsTrue(participantUsers.Any(u => u.Username == participantUsername));
        }
        
        [DataTestMethod]
        [DataRow("deleteuser", "Delete User", "delete@example.com", Roles.SurveyManager)]
        public async Task DeleteUserAsync_ExistingUser_RemovesUserAndReturnsSuccess(string username, string displayName, string email, Roles role)
        {
            // Arrange
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = "hashedpassword",
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, email } }
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.DeleteUserAsync(existingUser.Id);

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(Constants.Users.Deleted, result.Message);
            
            // Verify user was deleted from the database
            var deletedUser = await _userService.GetUserAsync(existingUser.Id);
            Assert.IsNull(deletedUser);
            
            // Verify log contains successful deletion message
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Information") && log.Contains("Deleted user") && log.Contains(username)),
                "Expected log indicating successful deletion");
        }

        [TestMethod]
        public async Task DeleteUserAsync_NonExistentUser_ReturnsFailure()
        {
            // Arrange
            var nonExistentUserId = Guid.NewGuid();

            // Act
            var result = await _userService.DeleteUserAsync(nonExistentUserId);

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.Users.NotFound, result.Message);
            
            // Verify warning was logged
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Warning") && log.Contains(nonExistentUserId.ToString())),
                "Expected warning log for non-existent user");
        }

        [DataTestMethod]
        [DataRow("resetuser", "Reset User", "reset@example.com", Roles.SurveyManager, "originalhash")]
        public async Task ResetPassword_ExistingUser_ReturnsSuccessWithNewPassword(string username, string displayName, string email, Roles role, string originalPasswordHash)
        {
            // Arrange
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = originalPasswordHash,
                RequirePasswordChange = false,
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, email } }
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.ResetPassword(existingUser.Id);

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(Constants.Users.PasswordUpdated, result.Message);
            Assert.IsFalse(string.IsNullOrEmpty(result.Data), "A new password should be returned");
            
            // Verify user's password was updated in the database
			await using var verifyContext = new SurveyDbContext(_options);
			var updatedUser = await verifyContext.Users.FindAsync(existingUser.Id);
			Assert.IsNotNull(updatedUser);
            Assert.AreNotEqual(originalPasswordHash, updatedUser.PasswordHash, "Password hash should be changed");
            Assert.IsTrue(updatedUser.RequirePasswordChange, "User should be required to change password");
            
            // Verify log contains password reset message
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Information") && log.Contains("Reset password") && log.Contains(username)),
                "Expected log indicating successful password reset");
        }

        [TestMethod]
        public async Task ResetPassword_NonExistentUser_ReturnsFailure()
        {
            // Arrange
            var nonExistentUserId = Guid.NewGuid();

            // Act
            var result = await _userService.ResetPassword(nonExistentUserId);

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.Users.NotFound, result.Message);
            
            // Verify warning was logged
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Warning") && log.Contains(nonExistentUserId.ToString())), "Expected warning log for non-existent user");
        }

        [DataTestMethod]
        [DataRow("emailuser", "Email User", "original@example.com", "updated@example.com", Roles.SurveyManager)]
        public async Task UpdateEmail_ExistingUser_UpdatesEmailAndReturnsSuccess(string username, string displayName, string originalEmail, string newEmail, Roles role)
        {
            // Arrange
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = "hashedpassword",
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, originalEmail } }
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.UpdateEmail(existingUser.Id, newEmail);

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(Constants.Users.EmailUpdated, result.Message);
            
            // Verify email was updated in the database
			await using var verifyContext = new SurveyDbContext(_options);
			var updatedUser = await verifyContext.Users.FindAsync(existingUser.Id);
			Assert.IsNotNull(updatedUser);
            Assert.AreEqual(newEmail, updatedUser.Emails[ParticipationTypes.Manual]);
            
            // Verify log contains email update message
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Information") && log.Contains($"Updated {ParticipationTypes.Manual} email") && 
                                      log.Contains(originalEmail) && log.Contains(newEmail)),
                "Expected log indicating successful email update");
        }
        
        [DataTestMethod]
        [DataRow("multiemailuser", "Multi Email User", "manual@example.com", "slack@example.com", "newslack@example.com", Roles.SurveyManager)]
        public async Task UpdateEmail_WithParticipationType_UpdatesCorrectEmailType(string username, string displayName, string manualEmail, string slackEmail, string newSlackEmail, Roles role)
        {
            // Arrange
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = "hashedpassword",
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> 
                { 
                    { ParticipationTypes.Manual, manualEmail },
                    { ParticipationTypes.Slack, slackEmail }
                }
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.UpdateEmail(existingUser.Id, newSlackEmail, ParticipationTypes.Slack);

            // Assert
            Assert.IsTrue(result.Successful);
            
            // Verify only Slack email was updated in the database
			await using var verifyContext = new SurveyDbContext(_options);
			var updatedUser = await verifyContext.Users.FindAsync(existingUser.Id);
			Assert.IsNotNull(updatedUser);
            Assert.AreEqual(manualEmail, updatedUser.Emails[ParticipationTypes.Manual], "Manual email should remain unchanged");
            Assert.AreEqual(newSlackEmail, updatedUser.Emails[ParticipationTypes.Slack], "Slack email should be updated");
        }

        [TestMethod]
        public async Task UpdateEmail_NonExistentUser_ReturnsFailure()
        {
            // Arrange
            var nonExistentUserId = Guid.NewGuid();

            // Act
            var result = await _userService.UpdateEmail(nonExistentUserId, "newemail@example.com");

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.Users.NotFound, result.Message);
            
            // Verify warning was logged
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
            Assert.IsTrue(logs.Any(log => log.Contains("Warning") && log.Contains(nonExistentUserId.ToString())), "Expected warning log for non-existent user");
        }

        [DataTestMethod]
        [DataRow("passworduser", "Password User", "password@example.com", "originalhash", "NewPassword123!", Roles.SurveyManager)]
        public async Task ChangePasswordAsync_ExistingUser_UpdatesPasswordAndReturnsSuccess(
			string username, string displayName, string email, string originalPasswordHash, string newPassword, Roles role)
        {
            // Arrange
            var existingUser = new User
            {
                Username = username,
                DisplayName = displayName,
                PasswordHash = originalPasswordHash,
                RequirePasswordChange = true,
                Role = role,
                Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, email } }
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.ChangePasswordAsync(existingUser.Id, newPassword);

            // Assert
            Assert.IsTrue(result.Successful);
            Assert.AreEqual(Constants.Users.PasswordUpdated, result.Message);
            Assert.AreEqual("/login", result.Data, "Redirect path should be returned");
            
            // Verify password was updated and flag reset in the database
			await using var verifyContext = new SurveyDbContext(_options);
            var updatedUser = await verifyContext.Users.FindAsync(existingUser.Id);
            Assert.IsNotNull(updatedUser);
            Assert.AreNotEqual(originalPasswordHash, updatedUser.PasswordHash, "Password hash should be changed");
            Assert.IsFalse(updatedUser.RequirePasswordChange, "RequirePasswordChange flag should be reset");
            
            // Verify log contains password change message
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Information") &&
										  log.Contains("changed their password") &&
										  log.Contains(username)),
						  "Expected log indicating successful password change");
		}

        [TestMethod]
        public async Task ChangePasswordAsync_NonExistentUser_ReturnsFailure()
        {
            // Arrange
            var nonExistentUserId = Guid.NewGuid();

            // Act
            var result = await _userService.ChangePasswordAsync(nonExistentUserId, "NewPassword123!");

            // Assert
            Assert.IsFalse(result.Successful);
            Assert.AreEqual(Constants.Users.NotFound, result.Message);
            
            // Verify warning was logged
            var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Warning") &&
										  log.Contains(nonExistentUserId.ToString())),
						  "Expected warning log for non-existent user");
		}
    }
}