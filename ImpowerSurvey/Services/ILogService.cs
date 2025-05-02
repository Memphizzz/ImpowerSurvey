using ImpowerSurvey.Components.Model;

namespace ImpowerSurvey.Services
{
	/// <summary>
	/// Interface for logging service that handles logging to both database and structured logs
	/// </summary>
	public interface ILogService
	{
		/// <summary>
		/// Logs a message to both the database and the ILogger
		/// </summary>
		Task<ServiceResult> LogAsync(LogSource source, LogLevel level, string message, bool containsIdentityData = false, bool containsResponseData = false, object data = null);
		
		/// <summary>
		/// Logs a ServiceResult object
		/// </summary>
		Task LogServiceResultAsync(ServiceResult result, LogSource source, bool containsIdentityData = false, bool containsResponseData = false);
		
		/// <summary>
		/// Logs an exception
		/// </summary>
		Task LogExceptionAsync(Exception ex, LogSource source, string context = null, bool containsIdentityData = false, bool containsResponseData = false);
		
		/// <summary>
		/// Gets logs filtered by criteria
		/// </summary>
		Task<List<Log>> GetLogsAsync(DateTime? startDate = null, DateTime? endDate = null, 
			string level = null, string source = null, string user = null, int take = 100);
	}
}