using ImpowerSurvey.Components.Model;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;

namespace ImpowerSurvey.Services;

public class LogService(IDbContextFactory<SurveyDbContext> contextFactory, ILogger<LogService> logger) : ILogService
{
	// ReSharper disable ReplaceWithPrimaryConstructorParameter
	private readonly IDbContextFactory<SurveyDbContext> _contextFactory = contextFactory;
	private readonly ILogger _logger = logger;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter

	/// <summary>
	/// Logs a message to both the database and the ILogger
	/// </summary>
	public async Task<ServiceResult> LogAsync(LogSource source, LogLevel level, string message, bool containsIdentityData = false, bool containsResponseData = false, object data = null)
	{
		// Always log to ILogger
		LogToILogger(source, level, message);

		var resultId = 0;

		// For DelayedSubmissionService, we limit logging to respect SHIELD
		if (source == LogSource.DelayedSubmissionService)
		{
			// Only log significant events or errors, never response data details
			if (level is LogLevel.Error or LogLevel.Critical)
			{
				// Only log error type, not details that might leak response data
				var sanitizedMessage = SanitizeDelayedSubmissionMessage(message);
				resultId = await LogToDatabaseAsync(source, level, sanitizedMessage);
			}
		}
		else
			// Log to database for other services
			resultId = await LogToDatabaseAsync(source, level, message, null, containsIdentityData, containsResponseData, data);

		return ServiceResult.Success($"Your error has been logged with ID: {resultId}");
	}

	/// <summary>
	/// Logs directly to the database
	/// </summary>
	private async Task<int> LogToDatabaseAsync(LogSource source, LogLevel level, string message, string username = null,
									      bool containsIdentityData = false, bool containsResponseData = false, object data = null)
	{
		try
		{
			var logEntry = new Log
			{
				Source = Constants.IsDebug ? source.ToString() : $"{LeaderElectionService.InstanceId}_{source}",
				Level = level.ToString(),
				Message = message,
				User = username,
				ContainsIdentityData = containsIdentityData,
				ContainsResponseData = containsResponseData
			};

			if (data != null)
				logEntry.SetData(data);

			await using var dbContext = await _contextFactory.CreateDbContextAsync();
			await dbContext.Logs.AddAsync(logEntry);
			await dbContext.SaveChangesAsync();
			return logEntry.Id;
		}
		catch (Exception ex)
		{
			// If database logging fails, at least try to log the error to ILogger
			_logger.LogError(ex, "Failed to write log to database: {Message}", ex.Message);
			return -1;
		}
	}

	/// <summary>
	/// Logs to the ILogger
	/// </summary>
	private void LogToILogger(LogSource source, LogLevel level, string message)
	{
		var scopedMessage = $"[{LeaderElectionService.InstanceId}] [{source}] {message}";

		switch (level)
		{
			case LogLevel.Information:
				_logger.LogInformation(scopedMessage);
				break;
			case LogLevel.Warning:
				_logger.LogWarning(scopedMessage);
				break;
			case LogLevel.Error:
				_logger.LogError(scopedMessage);
				break;
			case LogLevel.Debug:
				_logger.LogDebug(scopedMessage);
				break;
			case LogLevel.Critical:
				_logger.LogCritical(scopedMessage);
				break;
			default:
				_logger.LogInformation(scopedMessage);
				break;
		}
	}

	/// <summary>
	/// Sanitizes messages from the DelayedSubmissionService to ensure SHIELD compliance
	/// This method prevents any potentially identifiable or response data from being logged
	/// by filtering messages containing sensitive keywords and truncating exception details
	/// </summary>
	/// <param name="message">The original log message</param>
	/// <returns>A sanitized version of the message safe for logging</returns>
	private static string SanitizeDelayedSubmissionMessage(string message)
	{
		// Don't log specific response content from DSS
		if (message.Contains("response content") || message.Contains("answer") ||
			message.Contains("response data") || message.Contains("survey response"))
			return "Error in DelayedSubmissionService (specific details omitted for SHIELD compliance)";

		// Only log general information about the operation by:
		// 1. Replacing "Exception" with a more generic term
		// 2. Truncating at the first colon to remove potentially sensitive details
		return message.Replace("Exception", "Error occurred").Split(':')[0];
	}

	/// <summary>
	/// Logs a ServiceResult object
	/// </summary>
	public async Task LogServiceResultAsync(ServiceResult result, LogSource source, bool containsIdentityData = false, bool containsResponseData = false)
	{
		var level = result.Successful ? LogLevel.Information : LogLevel.Warning;
		await LogAsync(source, level, result.Message, containsIdentityData, containsResponseData);
	}

	/// <summary>
	/// Logs an exception
	/// </summary>
	public async Task LogExceptionAsync(Exception ex, LogSource source, string context = null, bool containsIdentityData = false, bool containsResponseData = false)
	{
		var message = context != null ? $"{context}: {ex.Message}" : ex.Message;
		await LogAsync(source, LogLevel.Error, message, containsIdentityData, containsResponseData, new { ExceptionType = ex.GetType().Name, ex.StackTrace });
	}

	/// <summary>
	/// Gets logs filtered by criteria
	/// </summary>
	public async Task<List<Log>> GetLogsAsync(DateTime? startDate = null, DateTime? endDate = null,
		string level = null, string source = null, string user = null, int take = 100)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		var query = dbContext.Logs.AsQueryable();

		// Apply filters
		if (startDate.HasValue)
			query = query.Where(l => l.Timestamp >= startDate.Value);

		if (endDate.HasValue)
			query = query.Where(l => l.Timestamp <= endDate.Value);

		if (!string.IsNullOrEmpty(level))
			query = query.Where(l => l.Level == level);

		if (!string.IsNullOrEmpty(source))
			query = query.Where(l => l.Source.Contains(source));

		if (!string.IsNullOrEmpty(user))
			query = query.Where(l => l.User == user);

		// Order by timestamp descending (newest first) and limit results
		return await query.OrderByDescending(l => l.Timestamp).Take(take).ToListAsync();
	}
	
	/// <summary>
	/// Gets an enum's description attribute value
	/// </summary>
	public static string GetEnumDescription(Enum value)
	{
		var field = value.GetType().GetField(value.ToString());
		if (field != null && field.GetCustomAttributes(typeof(DescriptionAttribute), false) is DescriptionAttribute[] { Length: > 0 } attributes)
			return attributes[0].Description;
		return value.ToString();
	}
}