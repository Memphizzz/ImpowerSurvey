using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.EntityFrameworkCore;

namespace ImpowerSurvey.Tests.Services
{
	// This class provides a testing-friendly wrapper around DelayedSubmissionService
	// since the original class has non-virtual methods that can't be mocked directly
	public class TestDelayedSubmissionService : DelayedSubmissionService
	{
		public bool ThrowExceptionOnFlush { get; set; } = false;
		public int FlushPendingResponsesReturnValue { get; set; }
		private readonly Mock<ILeaderElectionService> _mockLeaderElectionService;

		public TestDelayedSubmissionService(IDbContextFactory<SurveyDbContext> contextFactory, DssConfiguration config, ILogService logService)
			: base(contextFactory, config, logService, CreateMockLeaderElectionService().Object, CreateMockClaudeService().Object, null, null, null)
		{
			_mockLeaderElectionService = CreateMockLeaderElectionService();

			// By default, make this service the leader for testing
			SetLeadership(true);
		}
		
		private static Mock<IClaudeService> CreateMockClaudeService()
		{
			var mock = new Mock<IClaudeService>();
			
			// Setup mock to return the same text for anonymization (for testing)
			mock.Setup(m => m.AnonymizeTextAsync(It.IsAny<string>()))
				.Returns<string>(text => Task.FromResult(text));
				
			return mock;
		}

		private static Mock<ILeaderElectionService> CreateMockLeaderElectionService()
		{
			var mock = new Mock<ILeaderElectionService>();
			mock.Setup(m => m.InstanceId).Returns("test-instance-id");
			mock.Setup(m => m.IsLeader).Returns(true);
			return mock;
		}

		// Helper method to simulate leadership changes in tests
		public void SetLeadership(bool isLeader)
		{
			_mockLeaderElectionService.Setup(les => les.IsLeader).Returns(isLeader);
		}

		// Override the FlushPendingResponses method (using "new" since it's not virtual)
		public new Task<int> FlushPendingResponses(Guid surveyId)
		{
			if (ThrowExceptionOnFlush)
				throw new InvalidOperationException("Test exception");

			return Task.FromResult(FlushPendingResponsesReturnValue);
		}

		// Implement IHostedService methods (required for the test)
		public new Task StartAsync(CancellationToken cancellationToken)
		{
			// No-op for test implementation
			return Task.CompletedTask;
		}

		public new Task StopAsync(CancellationToken cancellationToken)
		{
			// No-op for test implementation
			return Task.CompletedTask;
		}
	}
}
