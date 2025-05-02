using System.ComponentModel.DataAnnotations.Schema;

namespace ImpowerSurvey.Components.Model;

public class Question
{
	public int Id { get; set; }
	public Guid SurveyId { get; set; }
	public string Text { get; set; }
	public QuestionTypes Type { get; set; }
	public List<QuestionOption> Options { get; set; } = [];
	public Survey Survey { get; set; }
}

public class QuestionOption
{
	public int Id { get; set; }
	public int QuestionId { get; set; }
	public string Text { get; set; }
	public Question Question { get; set; }

	[NotMapped]
	public bool IsChecked { get; set; }
}
