namespace ImpowerSurvey.Components.Model;

public class SurveyStartInfo
{
	public int ParticipantCount { get; set; } = 100;
	public ParticipationTypes ParticipationType { get; set; }
	public List<string> ParticipantList { get; set; }
	public Guid ManagerId { get; set; }
	public string ManagerDisplayName { get; set; }
	public string ManagerEmail { get; set; }
	public bool HasStartDate { get; set; }
	public DateTime StartDate { get; set; }
	public bool HasEndDate { get; set; }
	public DateTime EndDate { get; set; }
}
