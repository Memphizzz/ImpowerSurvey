using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ImpowerSurvey.Tests
{
    /// <summary>
    /// TestContext logger for MSTest that captures log messages during test execution
    /// </summary>
    public class TestContextLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly TestContext _testContext;
        
        // Static collection to store all log messages for the current test run
        private static readonly ConcurrentDictionary<string, List<string>> _testLogs = new();
        
        public TestContextLogger(string categoryName, TestContext testContext)
        {
            _categoryName = categoryName;
            _testContext = testContext;
            
            // Initialize log storage for this test
            if (_testContext != null)
            {
                _testLogs.TryAdd(_testContext.TestName, new List<string>());
            }
        }
        
        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        
        public bool IsEnabled(LogLevel logLevel) => true;
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
                
            var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            
            // Write to test context output
            if (_testContext != null)
            {
                _testContext.WriteLine(message);
                
                // Store in our collection for later access if needed
                if (_testLogs.TryGetValue(_testContext.TestName, out var logs))
                {
                    logs.Add(message);
                }
            }
            
            // If there's an exception, log the full details
            if (exception != null)
            {
                var exMessage = $"Exception: {exception.Message}\nStackTrace: {exception.StackTrace}";
                _testContext?.WriteLine(exMessage);
                
                if (_testLogs.TryGetValue(_testContext.TestName, out var logs))
                {
                    logs.Add(exMessage);
                }
            }
        }
        
        // Get all logs for a specific test
        public static List<string> GetLogsForTest(string testName)
        {
            return _testLogs.TryGetValue(testName, out var logs) ? logs : new List<string>();
        }
        
        // Clear logs for a specific test
        public static void ClearLogsForTest(string testName)
        {
            _testLogs.TryRemove(testName, out _);
        }
        
        private class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();
            
            private NullScope() { }
            
            public void Dispose() { }
        }
    }
    
    /// <summary>
    /// Generic version of TestContextLogger that implements ILogger<T>
    /// </summary>
    public class TestContextLogger<T> : TestContextLogger, ILogger<T>
    {
        public TestContextLogger(string categoryName, TestContext testContext) 
            : base(categoryName, testContext)
        {
        }
    }
    
    /// <summary>
    /// Logger factory that creates TestContextLogger instances
    /// </summary>
    public class TestContextLoggerFactory : ILoggerFactory
    {
        private readonly TestContext _testContext;
        
        public TestContextLoggerFactory(TestContext testContext)
        {
            _testContext = testContext;
        }
        
        public void Dispose() { }
        
        public ILogger CreateLogger(string categoryName)
        {
            return new TestContextLogger(categoryName, _testContext);
        }
        
        // Create a generic logger implementation
        public ILogger<T> CreateLogger<T>()
        {
            return new TestContextLogger<T>(typeof(T).FullName, _testContext);
        }
        
        public void AddProvider(ILoggerProvider provider) { }
    }
}
