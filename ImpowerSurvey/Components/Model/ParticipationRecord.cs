namespace ImpowerSurvey.Components.Model;

public class ParticipationRecord
{
	public Guid Id { get; set; }
	public Guid CompletionCodeId { get; set; }
	public string UsedBy { get; set; }
	public DateTime UsedAt { get; set; }

	public CompletionCode CompletionCode { get; set; }
}
