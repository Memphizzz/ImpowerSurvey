using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Pages;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;

namespace ImpowerSurvey.Components.Utilities;

/// <summary>
/// Helper class that contains shared code for both SurveyWizard and MobileSurveyWizard components
/// Implements composition rather than inheritance to avoid issues with partial classes
/// </summary>
public class SurveyWizard(ComponentBase parentComponent, Guid surveyId, NavigationManager navigationManager, DialogService dialogService, IJSUtilityService jsUtilityService,
						  NotificationService notificationService, AuthenticationStateProvider authStateProvider, bool isMobileView = false)
{
	public SurveyWizardController Controller { get; private set; }

	/// <summary>
    /// Initializes the controller
    /// </summary>
    public async Task InitializeAsync(SurveyService surveyService)
    {
        var authProvider = CustomAuthStateProvider.Get(authStateProvider);
        Controller = new SurveyWizardController(navigationManager, dialogService, jsUtilityService, notificationService, authProvider);
        
        var survey = await surveyService.GetSurveyByIdAsync(surveyId, true, true);
        await Controller.InitializeAsync(survey, isMobileView);
        Controller.OnPageChange += HandlePageChange;
    }
    
    /// <summary>
    /// Handles the page change event and triggers StateHasChanged on the parent component
    /// </summary>
    private void HandlePageChange()
	{
		switch (parentComponent)
		{
			case MobileSurveyWizardPage mobile:
				mobile.CallStateHasChanged();
				break;

			case SurveyWizardPage wizard:
				wizard.CallStateHasChanged();
				break;
		}
	}
    
    /// <summary>
    /// Prepares responses from the survey
    /// </summary>
	private List<Response> PrepareResponses()
    {
        return Controller.Survey.Questions.Select(question => new Response
                {
                    SurveyId = surveyId,
                    QuestionId = question.Id,
                    QuestionType = question.Type,
                    Answer = question.Type switch
                    {
                        QuestionTypes.Text           => Controller.Responses.GetValueOrDefault(question.Id, string.Empty),
                        QuestionTypes.SingleChoice   => Controller.SingleChoiceResponses.GetValueOrDefault(question.Id, string.Empty),
                        QuestionTypes.Rating         => Controller.RatingResponses.GetValueOrDefault(question.Id, 0).ToString(),
                        QuestionTypes.MultipleChoice => string.Join(", ", Controller.MultipleChoiceResponses.GetValueOrDefault(question.Id, [])),
                        var _                        => throw new ArgumentOutOfRangeException(nameof(question.Type), question.Type, "Unsupported question type")
                    }
                })
                .ToList();
    }
    
    /// <summary>
    /// Submits the survey with unified error handling
    /// </summary>
    public async Task SubmitSurveyAsync(SurveyService surveyService)
    {
		var busyText = "Preparing Responses";
        
        switch (parentComponent)
		{
			case SurveyWizardPage wizard:
				wizard.IsBusy = true;
				wizard.BusyText = busyText;
				break;

			case MobileSurveyWizardPage mobileWizard:
				mobileWizard.IsBusy = true;
				mobileWizard.BusyText = busyText;
				break;
		}
        
        await Task.Yield();

        try
        {
            var responses = PrepareResponses();
            busyText = "Submitting Survey";
            
            switch (parentComponent)
			{
				case SurveyWizardPage wizard:
					wizard.BusyText = busyText;
					break;

				case MobileSurveyWizardPage mobileWizard:
					mobileWizard.BusyText = busyText;
					break;
			}
                
            await Task.Yield();

            var state = await authStateProvider.GetAuthenticationStateAsync();
            var entryCode = state.User.Claims.FirstOrDefault(x => x.Type == nameof(EntryCode))?.Value;

            if (string.IsNullOrWhiteSpace(entryCode))
            {
                notificationService.Notify(new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "Authentication Failure",
                    Detail = Constants.UI.AuthenticationFailure,
                    Duration = -1
                });
                
                switch (parentComponent)
				{
					case SurveyWizardPage wizard:
						wizard.IsBusy = false;
						break;

					case MobileSurveyWizardPage mobileWizard:
						mobileWizard.IsBusy = false;
						break;
				}
                    
                return;
            }

            var result = await surveyService.SubmitSurveyAsync(surveyId, entryCode, responses);
            if (result.Successful)
            {
                Controller.CompletionCode = result.Data;
                await jsUtilityService.PreventTabClosure(Constants.UI.LeaveConfirmation);
            }
            else
				notificationService.NotifyFromServiceResult(result);
		}
        catch (Exception ex)
		{
			notificationService.NotifyFromException(ex);
		}
        finally
		{
			switch (parentComponent)
			{
				case SurveyWizardPage wizard:
					wizard.IsBusy = false;
					break;

				case MobileSurveyWizardPage mobileWizard:
					mobileWizard.IsBusy = false;
					break;
			}
		}
    }
    
    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        if (Controller != null)
        {
            Controller.ReleaseHooks();
            Controller.OnPageChange -= HandlePageChange;
        }
    }
}