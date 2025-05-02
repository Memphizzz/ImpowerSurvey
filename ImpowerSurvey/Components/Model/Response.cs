namespace ImpowerSurvey.Components.Model;

public class Response
{
	public int Id { get; set; }
	public Guid SurveyId { get; set; }
	public int QuestionId { get; set; }
	public string Answer { get; set; }
	public Question Question { get; set; }
	public Survey Survey { get; set; }
	public double Discrepancy { get; set; }
	public QuestionTypes QuestionType { get; set; }
}
