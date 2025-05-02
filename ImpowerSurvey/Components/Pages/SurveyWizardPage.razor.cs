using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components;

namespace ImpowerSurvey.Components.Pages;

public partial class SurveyWizardPage : IDisposable
{
	[Parameter]
	public Guid SurveyId { get; set; }

	public bool IsBusy { get; set; }
	public string BusyText { get; set; } = Constants.UI.Busy;
	private SurveyWizard Wizard { get; set; }

	private Survey Survey => Wizard?.Controller?.Survey;
	private string CompletionCode
	{
		get => Wizard?.Controller?.CompletionCode;
		set
		{
			if (Wizard?.Controller != null)
				Wizard.Controller.CompletionCode = value;
		}
	}

	private bool ShowRequired => Wizard?.Controller?.ShowRequired ?? false;
	private int CurrentPageIndex => Wizard?.Controller?.CurrentPageIndex ?? 0;
	private bool ClaudeEnabled { get; set; }
	
	protected override async Task OnInitializedAsync()
	{
		var authenticated = await CustomAuthStateProvider.Get(AuthStateProvider).GetIsAuthenticatedAsync();
		if (authenticated)
		{
			Wizard = new SurveyWizard(this, SurveyId, NavigationManager, DialogService, JSUtilityService, NotificationService, AuthStateProvider);
			await Wizard.InitializeAsync(SurveyService);
			ClaudeEnabled = await SettingsService.GetBoolSettingAsync(Constants.SettingsKeys.ClaudeEnabled);
		}
	}
	
	public void Dispose()
	{
		Wizard?.Dispose();
	}

	public void CallStateHasChanged()
	{
		StateHasChanged();
	}

	private Task SubmitSurvey() => Wizard.SubmitSurveyAsync(SurveyService);
	private void NavigateToPage(int pageIndex) => Wizard.Controller.NavigateToPage(pageIndex);
}