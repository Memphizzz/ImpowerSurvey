using System.ComponentModel;

namespace ImpowerSurvey.Components.Model;

public enum ParticipationTypes
{
	Manual,
	Slack
}

public enum SurveyStates
{
	Created,
	Scheduled,
	Running,
	Closed
}

public enum QuestionTypes
{
	Text,
	Rating,
	SingleChoice,
	MultipleChoice
}

public enum Roles
{
	[Description("Survey Participant")]
	SurveyParticipant = 0,

	[Description("Survey Manager")]
	SurveyManager = 1,

	[Description("Administrator")]
	Admin = 9
}

public enum SurveyEditorAction
{
	SaveChanges,
	AddQuestion,
	RemoveQuestion
}

public enum SettingType
{
	[Description("Text")]
	String,

	[Description("Number")]
	Number,

	[Description("Yes/No")]
	Boolean,

	[Description("Color")]
	Color,

	[Description("Image URL")]
	ImageUrl,

	[Description("Text Area")]
	Text,

	[Description("Selection")]
	Select
}

public enum ReportType
{
	Html,
	Csv
}

public enum ResultsTab
{
	Overview,
	Participation,
	Question
}

public enum LogSource
{
	[Description("Survey Service")]
	SurveyService,

	[Description("User Service")]
	UserService,

	[Description("Delayed Submission Service")]
	DelayedSubmissionService,

	[Description("Slack Service")]
	SlackService,

	[Description("Settings Service")]
	SettingsService,

	[Description("Survey Code Service")]
	SurveyCodeService,

	[Description("AuthState Provider")]
	AuthService,
	
	[Description("Survey Scheduler Service")]
	SurveySchedulerService,

	[Description("Leader Election Service")]
	LeaderElectionService,

	[Description("Claude Service")]
	ClaudeService,

	[Description("Authentication Middleware")]
	Security
}

/// <summary>
/// Types of inter-instance communication
/// </summary>
public enum InstanceCommunicationType
{
	/// <summary>
	/// Check if inter-instance communication works (used by followers on startup)
	/// </summary>
	NoOp,
	
	/// <summary>
	/// Transfer survey responses to leader
	/// </summary>
	TransferResponses,
	
	/// <summary>
	/// Notify leader to close a survey
	/// </summary>
	CloseSurvey
}