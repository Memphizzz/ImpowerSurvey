using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Collections;

namespace ImpowerSurvey.Services;

public class SettingsService(IDbContextFactory<SurveyDbContext> contextFactory, ILogService logService) : ISettingsService
{
	// ReSharper disable ReplaceWithPrimaryConstructorParameter
	private readonly IDbContextFactory<SurveyDbContext> _contextFactory = contextFactory;
	private readonly ILogService _logService = logService;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter
	
	// Settings that should not be logged when updated (to reduce log noise)
	private static readonly HashSet<string> NonLoggingSettings =
	[
		Constants.SettingsKeys.LeaderId,
		Constants.SettingsKeys.LeaderHeartbeat
	];
	
	public async Task<List<Setting>> GetAllSettingsAsync()
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		return await dbContext.Settings.OrderBy(s => s.Category).ThenBy(s => s.DisplayOrder).ToListAsync();
	}
	
	public async Task<List<Setting>> GetSettingsByCategoryAsync(string category)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		return await dbContext.Settings
			.Where(s => s.Category == category)
			.OrderBy(s => s.DisplayOrder)
			.ToListAsync();
	}
	
	public async Task<Setting> GetSettingByKeyAsync(string key)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		return await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
	}
	
	public async Task<string> GetSettingValueAsync(string key, string defaultValue = "")
	{
		var setting = await GetSettingByKeyAsync(key);
		return setting?.Value ?? defaultValue;
	}
	
	public async Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false)
	{
		var value = await GetSettingValueAsync(key);
		return !string.IsNullOrEmpty(value) && bool.TryParse(value, out var result) ? result : defaultValue;
	}
	
	public async Task<int> GetIntSettingAsync(string key, int defaultValue = 0)
	{
		var value = await GetSettingValueAsync(key);
		return !string.IsNullOrEmpty(value) && int.TryParse(value, out var result) ? result : defaultValue;
	}
	
	public async Task<ServiceResult> UpdateSettingAsync(string key, string value)
	{
		try
		{
			if (string.IsNullOrEmpty(key))
			{
				await _logService.LogAsync(LogSource.SettingsService, LogLevel.Warning, "Attempt to update setting with empty key");
				return ServiceResult.Failure("Setting key cannot be empty");
			}
				
			await using var dbContext = await _contextFactory.CreateDbContextAsync();
			
			var existingSetting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
			
			if (existingSetting != null)
			{
				var oldValue = existingSetting.Value;
				existingSetting.Value = value;
				await dbContext.SaveChangesAsync();
				
				if (!NonLoggingSettings.Contains(key))
					await _logService.LogAsync(LogSource.SettingsService, LogLevel.Information, $"Updated setting: '{key}' from '{oldValue}' to '{value}'");
				
				return ServiceResult.Success("Setting updated successfully");
			}
			
			await _logService.LogAsync(LogSource.SettingsService, LogLevel.Warning, $"Attempt to update non-existent setting with key: {key}");
			return ServiceResult.Failure("Setting not found");
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SettingsService, $"Error updating setting: {key}");
			return ServiceResult.Failure($"Error updating setting: {ex.Message}");
		}
	}
	
	/// <summary>
	/// Updates a setting only if it meets a specified condition, allowing for atomic operations in a distributed environment.
	/// </summary>
	/// <param name="key">The setting key</param>
	/// <param name="value">The new value to set</param>
	/// <param name="condition">A function that evaluates the current value and returns true if the update should proceed</param>
	/// <returns>True if the setting was updated, false if the condition was not met</returns>
	public virtual async Task<bool> TryUpdateSettingWithConditionAsync(string key, string value, Func<string, bool> condition)
	{
		if (string.IsNullOrEmpty(key))
		{
			await _logService.LogAsync(LogSource.SettingsService, LogLevel.Warning, "Attempt to conditionally update setting with empty key");
			return false;
		}

		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		var strategy = dbContext.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync();

			try
			{
				// Use UPDATE lock to ensure we have exclusive access during the check-and-set operation
				var existingSetting = await dbContext.Settings
													 .FromSqlRaw("SELECT * FROM \"Settings\" WHERE \"Key\" = @key FOR UPDATE", 
																 new NpgsqlParameter("@key", key))
													 .FirstOrDefaultAsync();
					
				if (existingSetting == null)
				{
					// Create new setting if it doesn't exist and condition allows
					if (condition(string.Empty))
					{
						var newSetting = new Setting 
						{ 
							Id = Guid.NewGuid(),
							Key = key, 
							Value = value,
							Type = SettingType.String,
							Category = "System",
							Description = $"Automatically created setting: {key}"
						};
						
						dbContext.Settings.Add(newSetting);
						await dbContext.SaveChangesAsync();
						await transaction.CommitAsync();
						
						if (!NonLoggingSettings.Contains(key))
							await _logService.LogAsync(LogSource.SettingsService, LogLevel.Information, 
													   $"Created and updated setting with conditional check: '{key}' to '{value}'");
						return true;
					}
				}
				else if (condition(existingSetting.Value))
				{
					// Update existing setting if condition passes
					var oldValue = existingSetting.Value;
					existingSetting.Value = value;
					await dbContext.SaveChangesAsync();
					await transaction.CommitAsync();
					
					if (!NonLoggingSettings.Contains(key))
						await _logService.LogAsync(LogSource.SettingsService, LogLevel.Information, 
											   $"Updated setting with conditional check: '{key}' from '{oldValue}' to '{value}'");
					return true;
				}
				
				// Condition failed, roll back and return false
				await transaction.RollbackAsync();
				return false;
			}
			catch (Exception ex)
			{
				await transaction.RollbackAsync();
				await _logService.LogAsync(LogSource.SettingsService, LogLevel.Error,
										   $"Error updating setting with conditional check: '{key}'. Error: {ex.Message}");
				return false;
			}
		});
	}
	
	// Initialize default settings if they don't exist
	public async Task InitializeDefaultSettingsAsync()
	{
		try
		{
			var defaultSettings = new List<Setting>
			{
				new()
				{
					Key = Constants.SettingsKeys.CompanyName,
					Value = "Impower.AI",
					Type = SettingType.String,
					Category = "Branding",
					Description = "Company name shown in the application",
					DisplayOrder = 0
				},
				new()
				{
					Key = Constants.SettingsKeys.CompanyLogoType,
					Value = "svg",
					Type = SettingType.Select,
					Category = "Branding",
					Description = "Logo icon type (svg or image)",
					DisplayOrder = 1
				},
				new()
				{
					Key = Constants.SettingsKeys.CompanyLogoUrl,
					Value = "/logo.png",
					Type = SettingType.ImageUrl,
					Category = "Branding",
					Description = "URL to the company logo icon image",
					DisplayOrder = 2
				},
				new()
				{
					Key = Constants.SettingsKeys.CompanyLogoSvg,
					Value = "<svg></svg>",
					Type = SettingType.Text,
					Category = "Branding",
					Description = "SVG markup for company logo icon",
					DisplayOrder = 3
				},
				new()
				{
					Key = Constants.SettingsKeys.CompanyNameLogoType,
					Value = "svg",
					Type = SettingType.Select,
					Category = "Branding",
					Description = "Company name logo type (svg or image)",
					DisplayOrder = 4
				},
				new()
				{
					Key = Constants.SettingsKeys.CompanyNameLogoUrl,
					Value = "/company-name.png",
					Type = SettingType.ImageUrl,
					Category = "Branding",
					Description = "URL to the company name logo image",
					DisplayOrder = 5
				},
				new()
				{
					Key = Constants.SettingsKeys.CompanyNameLogoSvg,
					Value = "<svg></svg>",
					Type = SettingType.Text,
					Category = "Branding",
					Description = "SVG markup for company name logo",
					DisplayOrder = 6
				},
				new()
				{
					Key = Constants.SettingsKeys.PrimaryColor,
					Value = "#096af2",
					Type = SettingType.Color,
					Category = "Branding",
					Description = "Primary theme color",
					DisplayOrder = 7
				},
				new()
				{
					Key = Constants.SettingsKeys.SchedulerLookAheadHours,
					Value = "24",
					Type = SettingType.Number,
					Category = "Scheduler",
					Description = "Hours to look ahead for creating timers",
					DisplayOrder = 0
				},
				new()
				{
					Key = Constants.SettingsKeys.SchedulerCheckIntervalHours,
					Value = "1",
					Type = SettingType.Number,
					Category = "Scheduler",
					Description = "Interval to check for new or upcoming surveys",
					DisplayOrder = 1
				},
				new()
				{
					Key = Constants.SettingsKeys.SlackReminderEnabled,
					Value = "true",
					Type = SettingType.Boolean,
					Category = "Notifications",
					Description = "Enable automatic Slack reminders",
					DisplayOrder = 0
				},
				new()
				{
					Key = Constants.SettingsKeys.SlackReminderHoursBefore,
					Value = "24",
					Type = SettingType.Number,
					Category = "Notifications",
					Description = "Hours before survey end to send reminders",
					DisplayOrder = 1
				},
				new()
				{
					Key = Constants.SettingsKeys.SlackReminderTemplate,
					Value = "Don't forget to complete the survey '{SurveyTitle}'. It closes in {HoursLeft} hours!",
					Type = SettingType.Text,
					Category = "Notifications",
					Description = "Template for reminder messages",
					DisplayOrder = 2
				},
				new()
				{
					Key = Constants.SettingsKeys.DefaultSurveyDuration,
					Value = "7",
					Type = SettingType.Number,
					Category = "Defaults",
					Description = "Default survey duration in days",
					DisplayOrder = 0
				},
				new()
				{
					Key = Constants.SettingsKeys.MinimumParticipants,
					Value = "5",
					Type = SettingType.Number,
					Category = "Defaults",
					Description = "Minimum number of participants for a survey",
					DisplayOrder = 1
				},

				// Claude AI settings
				new()
				{
					Key = Constants.SettingsKeys.ClaudeEnabled,
					Value = "true",
					Type = SettingType.Boolean,
					Category = "AI",
					Description = "Enable Claude AI features",
					DisplayOrder = 0
				},
				new()
				{
					Key = Constants.SettingsKeys.ClaudeModel,
					Value = "claude-3-7-sonnet-20250219",
					Type = SettingType.Select,
					Category = "AI",
					Description = "Claude model to use for AI features",
					DisplayOrder = 1
				},
				new()
				{
					Key = Constants.SettingsKeys.ClaudeAnonymizePrompt,
					Value =
						"Please anonymize the following survey response while preserving its meaning, sentiment, and core ideas. Remove any identifying speech patterns, uncommon word usage, specific references, or unique expressions that could identify the person. Maintain the same level of formality and overall tone.",
					Type = SettingType.Text,
					Category = "AI",
					Description = "Prompt for anonymizing survey responses",
					DisplayOrder = 2
				},
				new()
				{
					Key = Constants.SettingsKeys.ClaudeSummaryPrompt,
					Value = "Please analyze the following survey data and provide a comprehensive summary. Include key trends, participant sentiment, main themes, notable outliers, and actionable insights.",
					Type = SettingType.Text,
					Category = "AI",
					Description = "Base prompt for generating survey summaries",
					DisplayOrder = 3
				},

				// Leader election settings
				new()
				{
					Key = Constants.SettingsKeys.LeaderId,
					Value = string.Empty,
					Type = SettingType.String,
					Category = "System",
					Description = "ID of the current leader instance",
					DisplayOrder = 0
				},
				new()
				{
					Key = Constants.SettingsKeys.LeaderHeartbeat,
					Value = DateTime.UtcNow.ToString("o"),
					Type = SettingType.String,
					Category = "System",
					Description = "Last heartbeat from leader",
					DisplayOrder = 1
				},
				new()
				{
					Key = Constants.SettingsKeys.LeaderTimeout,
					Value = "2",
					Type = SettingType.Number,
					Category = "System",
					Description = "Minutes before leader timeout",
					DisplayOrder = 2
				},
				new()
				{
					Key = Constants.SettingsKeys.LeaderCheckIntervalSeconds,
					Value = "120",
					Type = SettingType.Number,
					Category = "System",
					Description = "Seconds between leader election checks",
					DisplayOrder = 3
				},
			};
			
			await using var dbContext = await _contextFactory.CreateDbContextAsync();
			foreach (var setting in defaultSettings)
			{
				var existingSetting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == setting.Key);
				if (existingSetting == null)
				{
					setting.Id = Guid.NewGuid();
					dbContext.Settings.Add(setting);
					await _logService.LogAsync(LogSource.SettingsService, LogLevel.Information, $"Adding default setting: {setting.Key} = '{setting.Value}' in category '{setting.Category}'");
				}
			}
			
			await dbContext.SaveChangesAsync();
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SettingsService, "Error initializing default settings");
			throw;
		}
	}
	
	/// <summary>
	/// Gets all environment variables
	/// </summary>
	/// <returns>A list of environment variables as key-value pairs</returns>
	public Task<List<KeyValuePair<string, string>>> GetEnvironmentVariablesAsync()
	{
		try
		{
			var vars = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
			var result = new List<KeyValuePair<string, string>>();
			
			foreach (DictionaryEntry entry in vars)
			{
				result.Add(new KeyValuePair<string, string>(entry.Key?.ToString() ?? string.Empty,
															entry.Value?.ToString() ?? string.Empty));
			}
			
			// Sort by key
			result.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.OrdinalIgnoreCase));
			
			return Task.FromResult(result);
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SettingsService, "Error retrieving environment variables");
			return Task.FromResult(new List<KeyValuePair<string, string>>());
		}
	}
}