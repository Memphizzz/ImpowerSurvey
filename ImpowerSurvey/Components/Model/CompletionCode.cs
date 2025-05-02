namespace ImpowerSurvey.Components.Model;

public class CompletionCode
{
	public Guid Id { get; set; }
	public Guid SurveyId { get; set; }
	public string Code { get; set; }
	public bool IsUsed { get; set; }
	public Survey Survey { get; set; }

	private CompletionCode() {}

	public static CompletionCode Create(Guid surveyId) => new() { Code = Guid.NewGuid().ToString("N"), IsUsed = false, SurveyId = surveyId };
}
