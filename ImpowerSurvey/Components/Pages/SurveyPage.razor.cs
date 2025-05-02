using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Radzen;

namespace ImpowerSurvey.Components.Pages;

public partial class SurveyPage
{
	[Parameter]
	public Guid? SurveyId { get; set; }

	private User Manager { get; set; }
	private Survey Survey { get; set; } 
	private string ManagerName => Manager?.DisplayName;
	private string PageTitle => SurveyId.HasValue ? "Edit Survey" : "Create New Survey";

	private string GenerateQuestionTitle(Question question) => $"Question {Survey.Questions.IndexOf(question) + 1}";
	private string GenerateOptionTitle(Question question, QuestionOption option) => $"Option {question.Options.IndexOf(option) + 1}";
	
	private IDisposable _registration;

	protected override async Task OnParametersSetAsync()
	{
		if (SurveyId.HasValue)
		{
			Survey = await SurveyService.GetSurveyByIdAsync(SurveyId.Value, true, true);

			if (Survey == null)
			{
				NavigationManager.NavigateTo("/surveys");
				return;
			}
		}
		else
			Survey = new Survey { Questions = [new Question()] };

		var authStateProvider = CustomAuthStateProvider.Get(AuthStateProvider);
		Manager = authStateProvider.GetUser();
		authStateProvider.SetCurrentSurvey(Survey);
		authStateProvider.OnSurveyEditorAction += HandleEditorAction;
		await base.OnParametersSetAsync();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		await base.OnAfterRenderAsync(firstRender);

		if (firstRender)
			_registration ??= NavigationManager.RegisterLocationChangingHandler(async context => await OnLocationChanging(context));
	}

	public void Dispose()
	{
		CustomAuthStateProvider.Get(AuthStateProvider).OnSurveyEditorAction -= HandleEditorAction;
		_registration?.Dispose();
	}
	private async Task OnLocationChanging(LocationChangingContext context)
	{
		var isNew = !SurveyId.HasValue;
		var isNavigatingToCreate = context.TargetLocation.Contains("/create-survey");
		var (message, title, okText, cancelText) = isNew
			? ("Are you sure you want to discard this Survey?", "Warning: Survey has not been saved!", "Leave", "Stay")
			: ("Do you want to save your changes?", "Save Changes?", "Discard", "Save");

		var result = await DialogService.Confirm(message, title, new ConfirmOptions
		{
			OkButtonText = okText,
			CancelButtonText = cancelText,
			AutoFocusFirstElement = false,
			ShowClose = false,
			CloseDialogOnEsc = false,
			CloseDialogOnOverlayClick = false
		});

		if (result == null || result.Value)
		{
			Survey = null;
			SurveyId = null;
			CustomAuthStateProvider.Get(AuthStateProvider).SetCurrentSurvey(null);
			
			if (!isNavigatingToCreate)
				_registration?.Dispose();
			return;
		}

		if (!isNew)
		{
			var updateResult = await SurveyService.UpdateSurveyAsync(Survey);
			if (updateResult.Successful && !isNavigatingToCreate)
				_registration?.Dispose();
		}
		else
			context.PreventNavigation();
	}

	private async Task HandleEditorAction(SurveyEditorAction action, object parameter)
	{
		switch (action)
		{
			case SurveyEditorAction.SaveChanges:
				await OnSubmit(Survey);
				break;
	            
			case SurveyEditorAction.AddQuestion:
				await AddQuestion();
				StateHasChanged();
				break;

			case SurveyEditorAction.RemoveQuestion:
				if (parameter is Question question)
					Survey.Questions.Remove(question);
				break;
		}
	}

	private async Task AddQuestion()
	{
		var question = new Question();
		Survey.Questions.Add(question);
		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromMilliseconds(250));
			await JSUtilityService.ScrollToElement(Constants.UI.EndElement);
		});
	}

	private void RemoveQuestion(Question question)
	{
		Survey.Questions.Remove(question);
	}

	private void AddOption(Question question)
	{
		question.Options.Add(new QuestionOption());
	}

	private void RemoveOption(Question question, QuestionOption option)
	{
		question.Options.Remove(option);
	}

	private void OnQuestionTypeChange(Question question)
	{
		if (question.Type != QuestionTypes.MultipleChoice && question.Type != QuestionTypes.SingleChoice)
			question.Options.Clear();
	}

	private async Task OnSubmit(Survey survey)
	{
		try
		{
			var authState = await AuthStateProvider.GetAuthenticationStateAsync();
			var user = authState.User;

			if (user.Identity is { IsAuthenticated: true })
			{
				if (!SurveyId.HasValue)
					survey.ManagerId = Manager.Id;
				
				bool success;
				string message;
				
				if (SurveyId.HasValue)
				{
					var updateResult = await SurveyService.UpdateSurveyAsync(survey);
					success = updateResult.Successful;
					message = updateResult.Message;
				}
				else
				{
					var createResult = await SurveyService.CreateSurveyAsync(survey);
					success = createResult.Successful;
					message = createResult.Message;
				}

				NotificationService.NotifyFromServiceResult(success ? ServiceResult.Success(message) : ServiceResult.Failure(message));
				if (success)
				{
					_registration?.Dispose();
					NavigationManager.NavigateTo("/surveys");
				}
			}
			else
			{
				_registration?.Dispose();
				NavigationManager.NavigateTo("/");
			}
		}
		catch (Exception ex)
		{
			NotificationService.NotifyFromServiceResult(ServiceResult.Failure(ex.Message));
		}
	}
}