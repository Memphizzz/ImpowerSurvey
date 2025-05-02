using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using Radzen;

namespace ImpowerSurvey.Components.Pages;

public partial class ReportsPage
{
    private bool IsBusy { get; set; }
    private List<Survey> ClosedSurveys { get; set; }
    private List<QuestionOption> SurveyOptions { get; set; } = [];
    private IEnumerable<string> SelectedSurveyValues { get; set; } = [];
    private List<Survey> SelectedSurveys { get; set; } = [];
    
    private bool CanGenerateReport => SelectedSurveys.Count >= 2;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await LoadSurveys();
    }
    
    private async Task LoadSurveys()
    {
        try
        {
            var allSurveys = await SurveyService.GetAllSurveysAsync(includeManager: true, includeQuestions: true, includeResponses: true, asNoTracking: true);
            
            // Filter to only show closed surveys
            ClosedSurveys = allSurveys
                .Where(s => s.State == SurveyStates.Closed)
                .OrderByDescending(s => s.CreationDate)
                .ToList();
                
            // Create options for selection
            SurveyOptions = ClosedSurveys
                .Select(s => new QuestionOption { 
                    Text = $"{s.Title} ({s.Questions.Count} questions, {s.Responses.Count} responses)",
                    IsChecked = false 
                })
                .ToList();
        }
        catch (Exception ex)
        {
            NotificationService.NotifyFromException(ex);
        }
    }
    
    private async Task OnSelectedSurveysChanged(IEnumerable<string> selectedValues)
    {
        SelectedSurveyValues = selectedValues;
        
        // Map back to the original surveys using the displayed text
        SelectedSurveys = new List<Survey>();
        foreach (var displayText in selectedValues)
        {
            var survey = ClosedSurveys.FirstOrDefault(s => 
                $"{s.Title} ({s.Questions.Count} questions, {s.Responses.Count} responses)" == displayText);
                
            if (survey != null)
                SelectedSurveys.Add(survey);
        }
            
        StateHasChanged();
    }
    
    private void RemoveSurvey(Survey survey)
    {
        var option = SurveyOptions.FirstOrDefault(o => 
            o.Text == $"{survey.Title} ({survey.Questions.Count} questions, {survey.UsedCompletionCodes} responses)");
            
        if (option != null)
        {
            option.IsChecked = false;
            SelectedSurveyValues = SurveyOptions
                .Where(o => o.IsChecked)
                .Select(o => o.Text);
                
            SelectedSurveys.Remove(survey);
            StateHasChanged();
        }
    }
    
    private async Task GenerateCombinedReport(ReportType reportType)
    {
        if (!CanGenerateReport || IsBusy)
            return;
            
        IsBusy = true;
        
        try
        {
            // Get full survey data with responses for each selected survey
            var surveysWithData = new List<Survey>();
            var allResponses = new List<List<Response>>();
            
            foreach (var survey in SelectedSurveys)
            {
                // Get detailed survey data including responses
                var surveyWithResponses = await SurveyService.GetSurveyResults(survey.Id);
                surveysWithData.Add(surveyWithResponses);
                allResponses.Add(surveyWithResponses.Responses);
            }
            
            // Generate the appropriate report
            var isDarkTheme = ThemeService.Theme == "material-dark";
            var data = reportType switch
            {
                ReportType.Html => await ReportBuilder.GenerateCombinedHtmlReportAsync(surveysWithData, allResponses, isDarkTheme),
                ReportType.Csv  => await ReportBuilder.GenerateCombinedCsvAsync(surveysWithData, allResponses),
                var _           => throw new ArgumentOutOfRangeException(nameof(reportType), reportType, null)
            };
            
            // Build a filename with the first few survey titles
            var reportTitle = string.Join("-", surveysWithData.Take(3).Select(s => s.Title[..Math.Min(s.Title.Length, 15)]));
            if (surveysWithData.Count > 3)
                reportTitle += "-and-others";
                
            // Download the file
            await JSUtilityService.DownloadHtmlFile($"Combined-{reportTitle}-{(isDarkTheme ? "Dark" : "Light")}", reportType.ToString().ToLower(), data);
            
            NotificationService.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Success",
                Detail = $"Combined report for {surveysWithData.Count} surveys has been generated",
                Duration = 5000
            });
        }
        catch (Exception ex)
        {
            NotificationService.NotifyFromException(ex);
        }
        finally
        {
            IsBusy = false;
            StateHasChanged();
        }
    }
}