using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;

namespace ImpowerSurvey.Components.Utilities
{
	/// <summary>
	/// Extension methods for the LogService that make it easier to use
	/// </summary>
	public static class LoggingExtensions
	{
		/// <summary>
		/// Logs a successful operation with a ServiceResult and returns the original result
		/// </summary>
		public static ServiceResult LogSuccess(this ServiceResult result, ILogService logService, LogSource source, 
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			if (result.Successful)
				_ = logService.LogAsync(source, LogLevel.Information, result.Message, containsIdentityData, containsResponseData);
				
			return result;
		}
		
		/// <summary>
		/// Logs a failed operation with a ServiceResult and returns the original result
		/// </summary>
		public static ServiceResult LogFailure(this ServiceResult result, ILogService logService, LogSource source, 
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			if (!result.Successful)
				_ = logService.LogAsync(source, LogLevel.Warning, result.Message, containsIdentityData, containsResponseData);
				
			return result;
		}
		
		/// <summary>
		/// Logs a ServiceResult (success or failure) and returns the original result
		/// </summary>
		public static ServiceResult LogResult(this ServiceResult result, ILogService logService, LogSource source, 
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			_ = logService.LogServiceResultAsync(result, source, containsIdentityData, containsResponseData);
			return result;
		}
		
		/// <summary>
		/// Logs a success operation with a DataServiceResult of type T and returns the original result
		/// </summary>
		public static DataServiceResult<T> LogSuccess<T>(this DataServiceResult<T> result, ILogService logService, LogSource source, 
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			if (result.Successful)
				_ = logService.LogAsync(source, LogLevel.Information, result.Message, containsIdentityData, containsResponseData);
				
			return result;
		}
		
		/// <summary>
		/// Logs a failed operation with a DataServiceResult of type T and returns the original result
		/// </summary>
		public static DataServiceResult<T> LogFailure<T>(this DataServiceResult<T> result, ILogService logService, LogSource source, 
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			if (!result.Successful)
				_ = logService.LogAsync(source, LogLevel.Warning, result.Message, containsIdentityData, containsResponseData);

			return result;
		}
		
		/// <summary>
		/// Logs an exception with an optional context message
		/// </summary>
		public static void LogException(this Exception ex, ILogService logService, LogSource source, string context = null,
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			_ = logService.LogExceptionAsync(ex, source, context, containsIdentityData, containsResponseData);
		}
		
		/// <summary>
		/// Logs a DataServiceResult (success or failure) of type T and returns the original result
		/// </summary>
		public static DataServiceResult<T> LogResult<T>(this DataServiceResult<T> result, ILogService logService, LogSource source, 
			bool containsIdentityData = false, bool containsResponseData = false)
		{
			_ = logService.LogServiceResultAsync(result, source, containsIdentityData, containsResponseData);
			return result;
		}
	}
}