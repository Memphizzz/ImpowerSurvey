using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using Microsoft.AspNetCore.Components;

namespace ImpowerSurvey.Components.Pages;

public partial class MobileSurveyWizardPage : IDisposable
{
    [Parameter]
    public Guid SurveyId { get; set; }

	public bool IsBusy { get; set; }
	public string BusyText { get; set; }
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
        Wizard = new SurveyWizard(this, SurveyId, NavigationManager, DialogService, JSUtilityService, NotificationService, AuthStateProvider, true);
        await Wizard.InitializeAsync(SurveyService);
        ClaudeEnabled = await SettingsService.GetBoolSettingAsync(Constants.SettingsKeys.ClaudeEnabled);
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Ensure page scrolls to top when first loaded
            await JSUtilityService.ScrollToTop();
        }
    }

	public void CallStateHasChanged()
	{
        StateHasChanged();
	}
    
    // Proxy methods
    private void NextPage() => Wizard.Controller.NextPage();
    private void PreviousPage() => Wizard.Controller.PreviousPage();
	private void NavigateToPage(int pageIndex) => Wizard.Controller.NavigateToPage(pageIndex);
	private Task SubmitSurvey() => Wizard.SubmitSurveyAsync(SurveyService);
    
    public void Dispose()
	{
	    Wizard?.Dispose();
	}
}