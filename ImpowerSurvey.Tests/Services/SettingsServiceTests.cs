using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
	[TestClass]
	public class SettingsServiceTests
	{
		private ISettingsService _settingsService;
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
			_settingsService = new SettingsService(_mockContextFactory.Object, _logService);
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
		public async Task InitializeDefaultSettingsAsync_EmptyDatabase_CreatesAllSettings()
		{
			// Arrange
			// Database starts empty

			// Act
			await _settingsService.InitializeDefaultSettingsAsync();

			// Assert
			await using var context = new SurveyDbContext(_options);
			var settings = await context.Settings.ToListAsync();

			// Verify all the expected settings are created
			Assert.IsTrue(settings.Count > 0, "Should create default settings");

			// Check for key settings from different categories
			Assert.IsNotNull(settings.FirstOrDefault(s => s.Key == Constants.SettingsKeys.CompanyName));
			Assert.IsNotNull(settings.FirstOrDefault(s => s.Key == Constants.SettingsKeys.PrimaryColor));
			Assert.IsNotNull(settings.FirstOrDefault(s => s.Key == Constants.SettingsKeys.SchedulerLookAheadHours));
			Assert.IsNotNull(settings.FirstOrDefault(s => s.Key == Constants.SettingsKeys.SlackReminderEnabled));
			Assert.IsNotNull(settings.FirstOrDefault(s => s.Key == Constants.SettingsKeys.DefaultSurveyDuration));

			// Verify log messages
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Information") && log.Contains("Adding default setting")));
		}

		[DataTestMethod]
		[DataRow(Constants.SettingsKeys.CompanyName, "Existing Company", SettingType.String, "Branding", "Existing setting that should not be overwritten")]
		public async Task InitializeDefaultSettingsAsync_WithExistingSettings_OnlyAddsNewSettings(string key, string value, SettingType type, string category, string description)
		{
			// Arrange
			// Add one existing setting
			var existingSetting = new Setting
			{
				Key = key,
				Value = value,
				Type = type,
				Category = category,
				Description = description
			};

			_dbContext.Settings.Add(existingSetting);
			await _dbContext.SaveChangesAsync();

			// Act
			await _settingsService.InitializeDefaultSettingsAsync();

			// Assert
			await using var context = new SurveyDbContext(_options);
			var settings = await context.Settings.ToListAsync();

			// Verify that settings were added, but the existing one wasn't overwritten
			Assert.IsTrue(settings.Count > 1, "Should have multiple settings");

			var companyNameSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
			Assert.IsNotNull(companyNameSetting);
			Assert.AreEqual(value, companyNameSetting.Value, "Existing setting should not be overwritten");
		}

		[DataTestMethod]
		[DataRow("TestKey", "TestValue", SettingType.String, "Test")]
		public async Task GetSettingValueAsync_ExistingSetting_ReturnsValue(string key, string value, SettingType type, string category)
		{
			// Arrange
			var setting = new Setting
			{
				Key = key,
				Value = value,
				Type = type,
				Category = category
			};

			_dbContext.Settings.Add(setting);
			await _dbContext.SaveChangesAsync();

			// Act
			var result = await _settingsService.GetSettingValueAsync(key);

			// Assert
			Assert.AreEqual(value, result);
		}

		[DataTestMethod]
		[DataRow("NonExistentKey", "DefaultValue")]
		public async Task GetSettingValueAsync_NonExistentSetting_ReturnsDefaultValue(string key, string defaultValue)
		{
			// Arrange - No settings in database

			// Act
			var result = await _settingsService.GetSettingValueAsync(key, defaultValue);

			// Assert
			Assert.AreEqual(defaultValue, result);
		}

		[DataTestMethod]
		[DataRow("UpdateTest", "OriginalValue", "NewValue", SettingType.String, "Test")]
		public async Task UpdateSettingAsync_ExistingSetting_UpdatesValue(string key, string originalValue, string newValue, SettingType type, string category)
		{
			// Arrange
			var setting = new Setting
			{
				Key = key,
				Value = originalValue,
				Type = type,
				Category = category
			};

			_dbContext.Settings.Add(setting);
			await _dbContext.SaveChangesAsync();

			// Act
			var result = await _settingsService.UpdateSettingAsync(key, newValue);

			// Assert
			Assert.IsTrue(result.Successful);

			// Verify the setting was updated in the database
			await using var context = new SurveyDbContext(_options);
			var updatedSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);

			Assert.IsNotNull(updatedSetting);
			Assert.AreEqual(newValue, updatedSetting.Value);

			// Verify log messages
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Information") &&
										  log.Contains(key) && log.Contains(originalValue) && log.Contains(newValue)));
		}

		[DataTestMethod]
		[DataRow("NonExistentKey", "NewValue")]
		public async Task UpdateSettingAsync_NonExistentSetting_ReturnsFailure(string key, string value)
		{
			// Arrange - No settings in database

			// Act
			var result = await _settingsService.UpdateSettingAsync(key, value);

			// Assert
			Assert.IsFalse(result.Successful);
			Assert.AreEqual("Setting not found", result.Message);

			// Verify log messages
			var logs = TestContextLogger.GetLogsForTest(TestContext.TestName);
			Assert.IsTrue(logs.Any(log => log.Contains("Warning") && log.Contains("non-existent setting")));
		}

		[DataTestMethod]
		[DataRow("BoolTest", "true", SettingType.Boolean, "Test")]
		public async Task GetBoolSettingAsync_ValidBoolSetting_ReturnsParsedValue(string key, string value, SettingType type, string category)
		{
			// Arrange
			var setting = new Setting
			{
				Key = key,
				Value = value,
				Type = type,
				Category = category
			};

			_dbContext.Settings.Add(setting);
			await _dbContext.SaveChangesAsync();

			// Act
			var result = await _settingsService.GetBoolSettingAsync(key);

			// Assert
			Assert.IsTrue(result);
		}

		[DataTestMethod]
		[DataRow("IntTest", "42", SettingType.Number, "Test")]
		public async Task GetIntSettingAsync_ValidIntSetting_ReturnsParsedValue(string key, string value, SettingType type, string category)
		{
			// Arrange
			var setting = new Setting
			{
				Key = key,
				Value = value,
				Type = type,
				Category = category
			};

			_dbContext.Settings.Add(setting);
			await _dbContext.SaveChangesAsync();

			// Act
			var result = await _settingsService.GetIntSettingAsync(key);

			// Assert
			Assert.AreEqual(42, result);
		}

		[TestMethod]
		public async Task GetSettingsByCategoryAsync_ReturnsSettingsInOrder()
		{
			// Arrange
			var settings = new List<Setting>
			{
				new()
				{
					Key = "Test1",
					Value = "Value1",
					Type = SettingType.String,
					Category = "TestCategory",
					DisplayOrder = 2
				},
				new()
				{
					Key = "Test2",
					Value = "Value2",
					Type = SettingType.String,
					Category = "TestCategory",
					DisplayOrder = 1
				},
				new()
				{
					Key = "Test3",
					Value = "Value3",
					Type = SettingType.String,
					Category = "OtherCategory",
					DisplayOrder = 0
				}
			};

			_dbContext.Settings.AddRange(settings);
			await _dbContext.SaveChangesAsync();

			// Act
			var result = await _settingsService.GetSettingsByCategoryAsync("TestCategory");

			// Assert
			Assert.AreEqual(2, result.Count, "Should return only settings from the specified category");
			Assert.AreEqual("Test2", result[0].Key, "Settings should be ordered by DisplayOrder");
			Assert.AreEqual("Test1", result[1].Key);
		}
	}
}