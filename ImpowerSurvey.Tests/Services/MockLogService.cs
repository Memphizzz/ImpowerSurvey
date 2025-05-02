using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.Extensions.Logging;

namespace ImpowerSurvey.Tests.Services
{
    /// <summary>
    /// Mock implementation of ILogService for testing
    /// </summary>
    public class MockLogService : ILogService
    {
        // Log collection to track calls
        public List<(LogSource Source, LogLevel Level, string Message)> Logs { get; } = new();
        
        /// <summary>
        /// Logs a message to an in-memory collection for testing
        /// </summary>
        public Task<ServiceResult> LogAsync(LogSource source, LogLevel level, string message,
            bool containsIdentityData = false, bool containsResponseData = false, object data = null)
        {
            Logs.Add((source, level, message));
            return Task.FromResult(ServiceResult.Success($"Log ID: {Logs.Count}"));
        }
        
        /// <summary>
        /// Logs a ServiceResult to an in-memory collection for testing
        /// </summary>
        public Task LogServiceResultAsync(ServiceResult result, LogSource source,
            bool containsIdentityData = false, bool containsResponseData = false)
        {
            var level = result.Successful ? LogLevel.Information : LogLevel.Warning;
            Logs.Add((source, level, result.Message));
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Logs an exception to an in-memory collection for testing
        /// </summary>
        public Task LogExceptionAsync(Exception ex, LogSource source, string context = null,
            bool containsIdentityData = false, bool containsResponseData = false)
        {
            var message = context != null ? $"{context}: {ex.Message}" : ex.Message;
            Logs.Add((source, LogLevel.Error, message));
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Gets logs from the in-memory collection
        /// </summary>
        public Task<List<Log>> GetLogsAsync(DateTime? startDate = null, DateTime? endDate = null,
            string level = null, string source = null, string user = null, int take = 100)
        {
            // Create Log objects from the in-memory collection
            var result = new List<Log>();
            
            foreach (var (logSource, logLevel, message) in Logs)
            {
                // Skip if filters don't match
                if (source != null && logSource.ToString() != source)
                    continue;
                    
                if (level != null && logLevel.ToString() != level)
                    continue;
                
                var log = new Log
                {
                    Id = result.Count + 1,
                    Source = logSource.ToString(),
                    Level = logLevel.ToString(),
                    Message = message,
                    Timestamp = DateTime.UtcNow
                };
                
                result.Add(log);
                
                if (result.Count >= take)
                    break;
            }
            
            return Task.FromResult(result);
        }
        
        /// <summary>
        /// Clears all logs from the in-memory collection
        /// </summary>
        public void ClearLogs()
        {
            Logs.Clear();
        }
    }
}