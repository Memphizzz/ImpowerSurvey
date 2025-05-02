using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    /// <summary>
    /// Test version of the SettingsService that works with in-memory database
    /// </summary>
    public class TestSettingsService : SettingsService
    {
        private readonly IDbContextFactory<SurveyDbContext> _contextFactory;
        private readonly ILogService _logService;
        
        public TestSettingsService(IDbContextFactory<SurveyDbContext> contextFactory, ILogService logService)
            : base(contextFactory, logService)
        {
            _contextFactory = contextFactory;
            _logService = logService;
        }
        
        /// <summary>
        /// Override the TryUpdateSettingWithConditionAsync method to make it work with in-memory database
        /// since FromSqlRaw is not supported by EF Core's in-memory provider
        /// </summary>
        public override async Task<bool> TryUpdateSettingWithConditionAsync(string key, string value, Func<string, bool> condition)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    await _logService.LogAsync(LogSource.SettingsService, LogLevel.Warning, "Attempt to conditionally update setting with empty key");
                    return false;
                }
                
                await using var dbContext = await _contextFactory.CreateDbContextAsync();
                
                // Get the setting - no need for locking in tests
                var existingSetting = await dbContext.Settings
                    .FirstOrDefaultAsync(s => s.Key == key);
                    
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
                        
                        // Log if not a non-logging setting
                        var nonLoggingSettings = new HashSet<string> { Constants.SettingsKeys.LeaderId, Constants.SettingsKeys.LeaderHeartbeat };
                        if (!nonLoggingSettings.Contains(key))
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
                    
                    // Log if not a non-logging setting
                    var nonLoggingSettings = new HashSet<string> { Constants.SettingsKeys.LeaderId, Constants.SettingsKeys.LeaderHeartbeat };
                    if (!nonLoggingSettings.Contains(key))
                        await _logService.LogAsync(LogSource.SettingsService, LogLevel.Information, 
                                               $"Updated setting with conditional check: '{key}' from '{oldValue}' to '{value}'");
                    return true;
                }
                
                // Condition failed
                return false;
            }
            catch (Exception ex)
            {
                await _logService.LogAsync(LogSource.SettingsService, LogLevel.Error, $"Error in conditional update of setting: {key}: {ex.Message}");
                return false;
            }
        }
    }
}