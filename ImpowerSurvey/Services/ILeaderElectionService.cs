namespace ImpowerSurvey.Services
{
    /// <summary>
    /// Service interface for distributed leader election in multi-instance environments
    /// </summary>
    public interface ILeaderElectionService
    {
		/// <summary>
		/// Gets the unique identifier for this application instance
		/// </summary>
		string InstanceId { get; }

        /// <summary>
        /// Gets a value indicating whether this instance is currently the leader
        /// </summary>
        bool IsLeader { get; }
        
        /// <summary>
        /// Gets a value indicating whether the service has completed its initialization
        /// </summary>
        bool IsReady { get; }

		/// <summary>
        /// Event triggered when the leadership status changes
        /// </summary>
        event Action<bool> OnLeadershipChanged;
    }
}