using ImpowerSurvey.Services;

namespace ImpowerSurvey.Tests.Services
{
    /// <summary>
    /// A test wrapper for the LeaderElectionService that allows controlling the leadership status
    /// </summary>
    public class LeaderElectionServiceWrapper : ILeaderElectionService
    {
        private bool _isLeader;
        
        public bool IsLeader => _isLeader;
        
        public string InstanceId { get; }
		public bool IsReady { get; }

		public event Action<bool> OnLeadershipChanged;
        
        public LeaderElectionServiceWrapper(bool isLeader = false)
        {
            _isLeader = isLeader;
            InstanceId = Guid.NewGuid().ToString();
        }
        
        /// <summary>
        /// Sets the leadership status for testing
        /// </summary>
        public void SetLeadership(bool isLeader)
        {
            if (_isLeader != isLeader)
            {
                _isLeader = isLeader;
                OnLeadershipChanged?.Invoke(_isLeader);
            }
        }
    }
}