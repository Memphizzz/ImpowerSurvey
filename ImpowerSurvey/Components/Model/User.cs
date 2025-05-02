using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImpowerSurvey.Components.Model;

public class User
{
	public Guid Id { get; set; }

	[Required]
	[StringLength(50)]
	public string Username { get; set; }

	public string DisplayName { get; set; }

	[Required]
	[StringLength(60)]
	public string PasswordHash { get; set; }

	[Required]
	public Roles Role { get; set; }

	[Required]
	public Dictionary<ParticipationTypes, string> Emails { get; set; } = new() { { ParticipationTypes.Manual, string.Empty } };

	[Required]
	public bool RequirePasswordChange { get; set; } = true;

	public List<Survey> ManagedSurveys { get; set; } = [];
	
	public string TimeZone { get; set; }

	[NotMapped]
	public string Email
	{
		get => Emails.Count == 1 ? Emails.First().Value : Emails.TryGetValue(ParticipationTypes.Manual, out var email) ? email : string.Empty;
		set => Emails[ParticipationTypes.Manual] = value;
	}
}
