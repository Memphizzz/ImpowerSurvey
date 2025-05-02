namespace ImpowerSurvey.Components.Model;

public class Survey
{
	public Guid Id { get; set; }
	public string Title { get; set; }
	public Guid ManagerId { get; set; }
	public User Manager { get; set; }
	public string Description { get; set; }
	public SurveyStates State { get; set; }
	public DateTime CreationDate { get; set; }
	public DateTime? ScheduledStartDate { get; set; }
	public DateTime? ScheduledEndDate { get; set; }
	public ParticipationTypes ParticipationType { get; set; }
	public List<string> Participants { get; set; } = [];
	public List<Question> Questions { get; set; } = [];
	public List<Response> Responses { get; set; } = [];
	public List<EntryCode> EntryCodes { get; set; } = [];
	public List<CompletionCode> CompletionCodes { get; set; } = [];
	
	// Statistics for participation tracking after survey is closed
	public int IssuedEntryCodesCount { get; set; } = 0;
	public int UsedEntryCodesCount { get; set; } = 0;
	public int CreatedCompletionCodesCount { get; set; } = 0;
	public int SubmittedCompletionCodesCount { get; set; } = 0;

	// Dynamic properties based on collections
	public int UsedEntryCodes => EntryCodes.Count(x => x.IsUsed);
	public int UsedCompletionCodes => CompletionCodes.Count(x => x.IsUsed);
}
