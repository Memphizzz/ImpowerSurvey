using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Radzen;

namespace ImpowerSurvey.Components.Utilities;

/// <summary>
/// Helper class for survey navigation functionality shared between SurveyWizard and MobileSurveyWizard
/// </summary>
public class SurveyWizardController
(
	NavigationManager navigationManager,
	DialogService dialogService,
	IJSUtilityService jsUtilityService,
	NotificationService notificationService,
	CustomAuthStateProvider authStateProvider)
{
	private IDisposable _locationChangingRegistration;
    
    public Survey Survey { get; private set; }
    public string CompletionCode { get; set; }
    public bool ShowRequired { get; private set; }
    public int CurrentPageIndex { get; private set; }
    
    // For handling responses
    public Dictionary<int, string> Responses { get; } = new();
    public Dictionary<int, int> RatingResponses { get; } = new();
    public Dictionary<int, string> SingleChoiceResponses { get; } = new();
    public Dictionary<int, IEnumerable<string>> MultipleChoiceResponses { get; } = new();

	/// <summary>
    /// Initializes the navigation helper with survey data
    /// </summary>
    public async Task InitializeAsync(Survey survey, bool isMobile = false)
    {
        // Subscribe to navigation state changes
        authStateProvider.SurveyNavigationStateChanged += HandleSurveyNavigationStateChanged;

		Survey = survey;

        if (Survey == null)
        {
            navigationManager.NavigateTo(isMobile ? "/mobile" : "/");
            return;
        }
        
        // Initialize responses for all question types
        foreach (var question in Survey.Questions)
            switch (question.Type)
            {
                case QuestionTypes.Text:
                    Responses[question.Id] = string.Empty;
                    break;

                case QuestionTypes.SingleChoice:
                    SingleChoiceResponses[question.Id] = string.Empty;
                    break;

                case QuestionTypes.MultipleChoice:
                    MultipleChoiceResponses[question.Id] = new List<string>();
                    break;

                case QuestionTypes.Rating:
                    RatingResponses[question.Id] = 0;
                    break;
            }
        
        authStateProvider.SetCurrentSurvey(Survey);
        authStateProvider.SetCurrentPageIndex(0);
        
        // Set up location changing registration
        _locationChangingRegistration = navigationManager.RegisterLocationChangingHandler(async context => await OnLocationChanging(context));
    }
    
    /// <summary>
    /// Handles survey navigation state changes
    /// </summary>
    private void HandleSurveyNavigationStateChanged(Survey survey, int pageIndex)
    {
        switch (pageIndex)
        {
            case 0:
                // Welcome page
                CurrentPageIndex = 0;
                NotifyPageChanged();
                break;

            case >= 1 when pageIndex <= Survey.Questions.Count:
                CurrentPageIndex = pageIndex;
                NotifyPageChanged();
                break;

            default:
            {
                if (pageIndex == Survey.Questions.Count + 1)
                {
                    // Submit page
                    CurrentPageIndex = Survey.Questions.Count + 1;
                    NotifyPageChanged();
                }

                break;
            }
        }
    }
    
    /// <summary>
    /// Handles the browser location changing event
    /// </summary>
    private async Task OnLocationChanging(LocationChangingContext context)
    {
        var result = await dialogService.Confirm(
            CompletionCode == null ? Constants.Survey.LeaveSurveyNote : Constants.Survey.LeaveCompleteSurveyWarning,
            CompletionCode == null ? Constants.Survey.LeaveSurveyNoteTitle : Constants.Survey.LeaveCompleteSurveyWarningTitle,
            new ConfirmOptions
            {
                OkButtonText = "Stay",
                CancelButtonText = "Leave",
                AutoFocusFirstElement = false,
                ShowClose = false,
                CloseDialogOnEsc = false,
                CloseDialogOnOverlayClick = false
            });
            
        if (result == null || result.Value)
            context.PreventNavigation();
        else
            await ReleaseHooksAsync();
    }
    
    /// <summary>
    /// Releases all hooks and registrations
    /// </summary>
    public void ReleaseHooks()
    {
        // Only dispose what's safe to dispose synchronously without JS interop
        _locationChangingRegistration?.Dispose();
        authStateProvider.SurveyNavigationStateChanged -= HandleSurveyNavigationStateChanged;
        
        // Don't attempt JS interop operations here as they're unsafe during prerendering
    }

    /// <summary>
    /// Releases all hooks and registrations asynchronously
    /// </summary>
	private async Task ReleaseHooksAsync()
    {
        _locationChangingRegistration?.Dispose();
        authStateProvider.SurveyNavigationStateChanged -= HandleSurveyNavigationStateChanged;
        await jsUtilityService.AllowTabClosure();
        await authStateProvider.LogoutAsync();
    }
    
    /// <summary>
    /// Validates if navigation to the next question is allowed
    /// </summary>
    public bool CanGoNext(Question question)
    {
        if (question is { Type: QuestionTypes.Rating })
        {
            var anyResponse = RatingResponses.Any(x => x.Value != 0);
            if(anyResponse && RatingResponses[question.Id] == 0)
            {
                notificationService.Notify(NotificationSeverity.Warning, Constants.UI.Warning, Constants.Survey.NeedAnswersWarning, 5000);
                ShowRequired = true;
                return false;
            }
        }

        ShowRequired = false;
        return true;
    }
    
    /// <summary>
    /// Navigates to the next page 
    /// </summary>
    public void NextPage()
    {
        if (CurrentPageIndex > 0 && CurrentPageIndex <= Survey?.Questions.Count)
        {
            var question = Survey.Questions.ElementAt(CurrentPageIndex - 1);
            if (!CanGoNext(question))
                return;
        }
        
        NavigateToPage(CurrentPageIndex + 1);
    }
    
    /// <summary>
    /// Navigates to the previous page
    /// </summary>
    public void PreviousPage()
    {
        NavigateToPage(CurrentPageIndex - 1);
    }
    
    /// <summary>
    /// Navigates to a specific page
    /// </summary>
    public void NavigateToPage(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex <= Survey.Questions.Count + 1)
        {
            CurrentPageIndex = pageIndex;
            authStateProvider.SetCurrentPageIndex(pageIndex);
            NotifyPageChanged();
        }
    }
    
    /// <summary>
    /// Event that is triggered when the page index changes
    /// </summary>
    public event Action OnPageChange;
    
    /// <summary>
    /// Triggers the page change event
    /// </summary>
    private void NotifyPageChanged()
    {
        OnPageChange?.Invoke();
    }
}