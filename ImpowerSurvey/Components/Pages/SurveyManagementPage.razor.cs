using ImpowerSurvey.Components.Controls;
using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using ImpowerSurvey.Services;
using Radzen;

namespace ImpowerSurvey.Components.Pages;

public partial class SurveyManagementPage : IDisposable
{
	private bool IsBusy { get; set; }
	private CustomAuthStateProvider _authStateProvider;
	
	private List<Survey> PlannedSurveys { get; set; } = [];
	private List<Survey> RunningSurveys { get; set; } = [];
	private List<Survey> CompletedSurveys { get; set; } = [];
	private Survey SelectedSurvey { get; set; }

	protected override async Task OnInitializedAsync()
	{
		await base.OnInitializedAsync();
		_authStateProvider = CustomAuthStateProvider.Get(AuthStateProvider);
		_authStateProvider.DataGridStateChanged += HandleDataGridStateChanged;
		
		await LoadSurveys();
	}
	
	private async Task LoadSurveys()
	{
		var allSurveys = await SurveyService.GetAllSurveysAsync(includeManager: true, asNoTracking: true);
		
		// Categorize surveys client-side
		PlannedSurveys = allSurveys
			.Where(s => s.State is SurveyStates.Created or SurveyStates.Scheduled)
			.OrderByDescending(s => s.CreationDate)
			.ToList();
			
		RunningSurveys = allSurveys
			.Where(s => s.State == SurveyStates.Running)
			.OrderByDescending(s => s.CreationDate)
			.ToList();
			
		CompletedSurveys = allSurveys
			.Where(s => s.State == SurveyStates.Closed)
			.OrderByDescending(s => s.CreationDate)
			.ToList();
		
		// If we have a selected survey, update it from the refreshed data
		if (SelectedSurvey != null)
			SelectedSurvey = allSurveys.FirstOrDefault(s => s.Id == SelectedSurvey.Id);
	}

	private void HandleDataGridStateChanged()
	{
		_ = HandleDataGridStateChangedAsync();
	}

	private async Task HandleDataGridStateChangedAsync()
	{
		await InvokeAsync(async () => {
			await LoadSurveys();
			StateHasChanged();
		});
	}

	public void Dispose()
	{
		if (_authStateProvider != null)
			_authStateProvider.DataGridStateChanged -= HandleDataGridStateChanged;
		
	}

	private void NavigateToCreateSurvey()
	{
		NavigationManager.NavigateTo("/create-survey");
	}

	private void EditSurvey(Survey survey)
	{
		NavigationManager.NavigateTo($"/edit-survey/{survey.Id}");
	}

	private void RowSelect(Survey survey)
	{
		SelectedSurvey = survey;
		CustomAuthStateProvider.Get(AuthStateProvider).SetCurrentSurvey(survey);
		StateHasChanged();
	}

	private async void Debug()
	{
		IsBusy = true;
		await Task.Yield();

		await SlackService.Notify(string.Join(string.Empty, [.. Enumerable.Repeat("-", 100)]), Roles.Admin);

		IsBusy = false;
		StateHasChanged();
	}

	private async Task KickOffSurvey(Survey survey)
	{
		if (survey.State != SurveyStates.Created)
			throw new Exception("Invalid SurveyState!");

		var result = await DialogService.OpenAsync<StartSurveyDialog>("Survey Options",
																	  new Dictionary<string, object> { { "SurveyId", survey.Id } },
																	  new DialogOptions
																	  {
																		  AutoFocusFirstElement = true, CloseDialogOnEsc = true,
																		  CloseDialogOnOverlayClick = true, ShowClose = true
																	  });
		if (result is true)
			await LoadSurveys();
	}

	private async Task CloseSurvey(Survey survey)
	{
		IsBusy = true;
		await Task.Yield();

		var result = await DialogService.Confirm(string.Format(Constants.Survey.CloseWarning, survey.Title), "Warning", new ConfirmOptions { OkButtonText = "Yes, I'm sure", CancelButtonText = "Maybe not so much.." });
		if (result.HasValue && result.Value)
		{
			var closeResult = await SurveyService.CloseSurvey(survey.Id);
			NotificationService.NotifyFromServiceResult(closeResult);
			
			if (closeResult.Successful)
			{
				_authStateProvider.NotifyDataGridStateChanged();
				await LoadSurveys();
			}
		}

		IsBusy = false;
	}

	private async Task DeleteSurvey(Survey survey)
	{
		IsBusy = true;
		await Task.Yield();

		var result = await DialogService.Confirm(Constants.Survey.DeleteWarning1, "Warning", new ConfirmOptions { OkButtonText = "Yes, I'm sure", CancelButtonText = "Maybe not so much.." });
		if (result.HasValue && result.Value)
		{
			var confirm = await DialogService.Confirm(Constants.Survey.DeleteWarning2, "Final Warning", new ConfirmOptions { OkButtonText = "I said, YES!", CancelButtonText = "Get me out of here!" });
			if (confirm.HasValue && confirm.Value)
			{
				var deleteResult = await SurveyService.DeleteSurvey(survey.Id);
				NotificationService.NotifyFromServiceResult(deleteResult);
				
				if (deleteResult.Successful)
				{
					_authStateProvider.NotifyDataGridStateChanged();
					await LoadSurveys();
				}
			}
		}

		IsBusy = false;
	}

	private async Task DuplicateSurvey(Survey survey)
	{
		IsBusy = true;
		await Task.Yield();

		var result = await DialogService.Confirm($"Are you sure you want to duplicate Survey<br>'{survey.Title}'?", "Duplicate Survey", new ConfirmOptions { OkButtonText = "Yes, I'm sure", CancelButtonText = "I changed my mind.." });
		if (result.HasValue && result.Value)
		{
			var duplicateResult = await SurveyService.DuplicateSurvey(survey.Id);
			NotificationService.NotifyFromServiceResult(duplicateResult);
			
			if (duplicateResult.Successful)
			{
				_authStateProvider.NotifyDataGridStateChanged();
				await LoadSurveys();
			}
		}

		IsBusy = false;
	}

	private async Task ShowParticipation(Survey survey)
	{
		await DialogService.OpenAsync<ParticipationDialog>("Survey Participation",
														   new Dictionary<string, object> { { "SurveyId", survey.Id } },
														   new DialogOptions
														   {
															   AutoFocusFirstElement = true, CloseDialogOnEsc = true,
															   CloseDialogOnOverlayClick = true, ShowClose = true
														   });
	}

	private void ShowResults(Survey survey)
	{
		NavigationManager.NavigateTo($"/survey-results/{survey.Id}");
	}
}