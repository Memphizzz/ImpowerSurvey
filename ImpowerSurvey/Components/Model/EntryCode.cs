namespace ImpowerSurvey.Components.Model;

public class EntryCode
{
	public Guid Id { get; set; }
	public Guid SurveyId { get; set; }
	public string Code { get; set; }
	public bool IsIssued { get; set; }
	public bool IsUsed { get; set; }
	public Survey Survey { get; set; }

	private EntryCode() {}

	public static EntryCode Create(Guid surveyId) => new() { Code = Guid.NewGuid().ToString("N"), IsUsed = false, IsIssued = false, SurveyId = surveyId };
}
