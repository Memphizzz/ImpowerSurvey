using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;

namespace ImpowerSurvey.Tests.Services
{
    // Interface for SettingsService to allow mocking
    public interface ISettingsServiceWrapper
    {
        Task<List<Setting>> GetAllSettingsAsync();
        Task<List<Setting>> GetSettingsByCategoryAsync(string category);
        Task<Setting> GetSettingByKeyAsync(string key);
        Task<string> GetSettingValueAsync(string key, string defaultValue = "");
        Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false);
        Task<int> GetIntSettingAsync(string key, int defaultValue = 0);
        Task<ServiceResult> UpdateSettingAsync(string key, string value);
        Task InitializeDefaultSettingsAsync();
    }

    // Wrapper for the actual SettingsService that implements the interface
    public class SettingsServiceWrapper : ISettingsServiceWrapper
    {
        private readonly SettingsService _settingsService;

        public SettingsServiceWrapper(SettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public Task<List<Setting>> GetAllSettingsAsync() => _settingsService.GetAllSettingsAsync();
        public Task<List<Setting>> GetSettingsByCategoryAsync(string category) => _settingsService.GetSettingsByCategoryAsync(category);
        public Task<Setting> GetSettingByKeyAsync(string key) => _settingsService.GetSettingByKeyAsync(key);
        public Task<string> GetSettingValueAsync(string key, string defaultValue = "") => _settingsService.GetSettingValueAsync(key, defaultValue);
        public Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false) => _settingsService.GetBoolSettingAsync(key, defaultValue);
        public Task<int> GetIntSettingAsync(string key, int defaultValue = 0) => _settingsService.GetIntSettingAsync(key, defaultValue);
        public Task<ServiceResult> UpdateSettingAsync(string key, string value) => _settingsService.UpdateSettingAsync(key, value);
        public Task InitializeDefaultSettingsAsync() => _settingsService.InitializeDefaultSettingsAsync();
    }
}