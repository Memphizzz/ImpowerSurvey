namespace ImpowerSurvey.Components.Model;

public class DelayedSubmissionStatus
{
	public int Pending { get; set; }
	public DateTime LastTime { get; set; }
	public DateTime NextTime { get; set; }
	public int LastAmount { get; set; }
	public int CurrentPercentage { get; set; }
	
	// Leader election properties
	public bool IsLeader { get; set; }
	public string InstanceId { get; set; } = string.Empty;
	
	// Response transfer status
	public bool HasTransferredResponses { get; set; }
}
