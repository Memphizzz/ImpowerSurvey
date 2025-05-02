namespace ImpowerSurvey.Components.Model;

public class UserInfo
{
	public string EntryCode { get; set; }
}

public record SurveyParticipationStats
{
	public int IssuedEntryCodes { get; init; }
	public int UsedEntryCodes { get; init; }
	public int SubmittedCompletionCodes { get; init; }
	public List<ParticipationRecord> ParticipationRecords { get; init; }
}

public class DssConfiguration
{
	public int MinPercentage { get; set; } = 30;
	public int MaxPercentage { get; set; } = 70;
	public int PercentageIncrement { get; set; } = 2;
	public int ResetChancePercentage { get; set; } = 5;
	public int MinimumSurveySubmissions { get; set; } = 3;
}

public class QuestionTypeCount
{
	public string Type { get; set; }
	public int Count { get; set; }
}
    
public class ParticipationCount
{
	public string Category { get; set; }
	public int Count { get; set; }
}
    
public class QuestionSummary
{
	public int Id { get; set; }
	public int Number { get; set; }
	public string QuestionText { get; set; }
	public string Type { get; set; }
	public int ResponseCount { get; set; }
}

public class ClaudeOptions
{
	public string ApiKey { get; set; }
	public string ModelName { get; set; } = "claude-3-7-sonnet-20250219";
}