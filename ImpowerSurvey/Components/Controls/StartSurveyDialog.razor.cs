using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;

namespace ImpowerSurvey.Components.Controls;

public partial class StartSurveyDialog
{
	[Parameter]
	public Guid SurveyId { get; set; }
	
	private User User { get; set; }
	private SurveyStartInfo Model { get; } = new();
	private bool IsBusy { get; set; }
	private RadzenTemplateForm<SurveyStartInfo> TemplateForm { get; set; }
	private RadzenTextArea TextAreaParticipants { get; set; }
	private RadzenTextBox TextBoxManagerEmail { get; set; }
	private string ValidationMessage { get; set; }
	private bool? IsManagerSlackVerified { get; set; }
	private string ParticipantsString { get; set; }
	private List<string> Participants { get; set; }
	private List<string> InvalidSlackUsers { get; set; }

	protected override async Task OnInitializedAsync()
	{
		await base.OnInitializedAsync();

		User = CustomAuthStateProvider.Get(AuthStateProvider).GetUser();
		Model.ManagerId = User.Id;
		Model.ManagerDisplayName = User.DisplayName;
		Model.ManagerEmail = User.Emails.TryGetValue(Model.ParticipationType, out var email) ? email : User.Email;
	}

	private async Task OnSubmit()
	{
		IsBusy = true;
		await Task.Yield();

		try
		{
			if (Model.ParticipationType == ParticipationTypes.Slack)
			{
				IsManagerSlackVerified = (await SlackService.VerifyParticipants([User.Email])).Count == 0;
				//Workaround for Blazor not allowing async Validators
				if (IsManagerSlackVerified.HasValue && !IsManagerSlackVerified.Value)
				{
					TemplateForm.EditContext.NotifyFieldChanged(TemplateForm.FindComponent(TextBoxManagerEmail.Name).FieldIdentifier);
					return;
				}

				InvalidSlackUsers = await SlackService.VerifyParticipants(Participants);
				//Workaround for Blazor not allowing async Validators
				if (InvalidSlackUsers.Count > 0)
				{
					TemplateForm.EditContext.NotifyFieldChanged(TemplateForm.FindComponent(TextAreaParticipants.Name).FieldIdentifier);
					return;
				}

				Model.ParticipantList = Participants;
			}

			var result = await SurveyService.KickOffSurvey(SurveyId, Model);
			NotificationService.Notify(result.Successful ? NotificationSeverity.Success : NotificationSeverity.Error, result.Message);
			DialogService.Close(result.Successful);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private void OnInvalidSubmit() { }

	private bool ParticipantListValidator()
	{
		ValidationMessage = string.Empty;

		if (InvalidSlackUsers?.Count > 0)
		{
			ValidationMessage = $"Invalid Slack Users: {string.Join(", ", InvalidSlackUsers)}";
			InvalidSlackUsers = null;
			return false;
		}

		if (string.IsNullOrWhiteSpace(ParticipantsString))
		{
			ValidationMessage = string.Empty;
			return false;
		}

		Participants = ParticipantsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		Participants = Participants.Select(x => x.ToLower()).ToList();

		var invalidEmails = Participants.Where(x => !x.IsValidEmail()).ToList();
		if (invalidEmails.Any())
		{
			ValidationMessage = $"Invalid Address(es): {string.Join(", ", invalidEmails)}";
			return false;
		}

		var duplicateEmails = Participants.GroupBy(x => x).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
		if (duplicateEmails.Any())
		{
			ValidationMessage = $"Duplicate Address(es): {string.Join(", ", duplicateEmails)}";
			return false;
		}

		return true;
	}
	
	private bool SlackEmailValidator()
	{
		if (string.IsNullOrWhiteSpace(User.Email))
			return true;

		var isValidEmail = User.Email.IsValidEmail();

		if (!isValidEmail)
		{
			ValidationMessage = string.IsNullOrWhiteSpace(User.Email) ? string.Empty : Constants.UI.InvalidEmailAddress;
			IsManagerSlackVerified = null;
			return false;
		}

		if (IsManagerSlackVerified.HasValue && !IsManagerSlackVerified.Value)
		{
			ValidationMessage = Constants.Slack.Errors.UserNotFound;
			IsManagerSlackVerified = null;
			return false;
		}

		ValidationMessage = string.Empty;
		return true;
	}

	private bool StartDateValidator()
	{
		if (Model.HasStartDate)
		{
			if (Model.StartDate == default)
				return false;

			return CustomAuthStateProvider.Get(AuthStateProvider).GetLocalNow().AddHours(1) <= Model.StartDate;
		}

		return true;
	}

	private bool EndDateValidator()
	{
		if (Model.HasStartDate && Model.HasEndDate)
			return Model.EndDate > Model.StartDate;

		return true;
	}
}
