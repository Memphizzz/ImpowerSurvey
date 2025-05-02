namespace ImpowerSurvey.Components.Model;

public class PendingBatch
{
	public List<Response> Responses { get; set; }
	public DateTime ScheduledSubmissionTime { get; set; }
}
