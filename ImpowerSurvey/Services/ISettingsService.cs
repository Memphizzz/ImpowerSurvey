using ImpowerSurvey.Components.Model;

namespace ImpowerSurvey.Services
{
	/// <summary>
	/// Interface for the settings management service
	/// </summary>
	public interface ISettingsService
	{
		/// <summary>
		/// Gets all available settings
		/// </summary>
		Task<List<Setting>> GetAllSettingsAsync();
		
		/// <summary>
		/// Gets settings filtered by category
		/// </summary>
		Task<List<Setting>> GetSettingsByCategoryAsync(string category);
		
		/// <summary>
		/// Gets a specific setting by its key
		/// </summary>
		Task<Setting> GetSettingByKeyAsync(string key);
		
		/// <summary>
		/// Gets a setting's value as a string, with an optional default value
		/// </summary>
		Task<string> GetSettingValueAsync(string key, string defaultValue = "");
		
		/// <summary>
		/// Gets a setting's value as a boolean, with an optional default value
		/// </summary>
		Task<bool> GetBoolSettingAsync(string key, bool defaultValue = false);
		
		/// <summary>
		/// Gets a setting's value as an integer, with an optional default value
		/// </summary>
		Task<int> GetIntSettingAsync(string key, int defaultValue = 0);
		
		/// <summary>
		/// Updates a setting with a new value
		/// </summary>
		Task<ServiceResult> UpdateSettingAsync(string key, string value);
		
		/// <summary>
		/// Updates a setting only if it meets a specified condition, allowing for atomic operations in a distributed environment
		/// </summary>
		/// <param name="key">The setting key</param>
		/// <param name="value">The new value to set</param>
		/// <param name="condition">A function that evaluates the current value and returns true if the update should proceed</param>
		/// <returns>True if the setting was updated, false if the condition was not met</returns>
		Task<bool> TryUpdateSettingWithConditionAsync(string key, string value, Func<string, bool> condition);
		
		/// <summary>
		/// Initializes default settings if they don't exist
		/// </summary>
		Task InitializeDefaultSettingsAsync();
		
		/// <summary>
		/// Gets all environment variables
		/// </summary>
		/// <returns>A list of environment variables as key-value pairs</returns>
		Task<List<KeyValuePair<string, string>>> GetEnvironmentVariablesAsync();
	}
}