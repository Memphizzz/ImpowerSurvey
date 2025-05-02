using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using System.Text;
using System.Web;

namespace ImpowerSurvey.Components.Utilities;

public interface IReportBuilder
{
	Task<string> GenerateHtmlReportAsync(Survey survey, List<Response> responses, bool isDarkTheme = false);
	Task<string> GenerateCombinedHtmlReportAsync(List<Survey> surveys, List<List<Response>> allResponses, bool isDarkTheme = false);
	Task<string> GenerateCsvAsync(Survey survey, List<Response> responses);
	Task<string> GenerateCombinedCsvAsync(List<Survey> surveys, List<List<Response>> allResponses);
}

public class ReportBuilder(IClaudeService claudeService) : IReportBuilder
{
	private const string CHART_COLORS = "'#FF6384','#36A2EB','#FFCE56','#4BC0C0','#9966FF','#FF9F40','#4BCFB5'";
	// ReSharper disable ReplaceWithPrimaryConstructorParameter
	private readonly IClaudeService _claudeService = claudeService;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter

	public async Task<string> GenerateHtmlReportAsync(Survey survey, List<Response> responses, bool isDarkTheme = false)
	{
		var report = new StringBuilder();
		report.AppendLine(GenerateHeader(survey));
		report.AppendLine(GenerateStyle(isDarkTheme));
		report.AppendLine(GenerateMetaInfo(survey));
		
		// Generate AI summary if Claude is available
		if (_claudeService != null)
		{
			try
			{
				var summary = await _claudeService.GenerateSurveySummaryAsync(survey, responses);
				if (!string.IsNullOrEmpty(summary))
					report.AppendLine(GenerateAISummary(summary));
			}
			catch
			{
				// If AI summary generation fails, we just skip it and continue with the rest of the report
			}
		}
		
		report.AppendLine(await GenerateQuestions(survey, responses));
		report.AppendLine("</body></html>");

		return report.ToString();
	}

	public async Task<string> GenerateCombinedHtmlReportAsync(List<Survey> surveys, List<List<Response>> allResponses, bool isDarkTheme = false)
	{
		if (surveys == null || surveys.Count == 0 || allResponses == null || surveys.Count != allResponses.Count)
			throw new ArgumentException("Surveys and responses must be non-empty and of equal count");
			
		var report = new StringBuilder();
		report.AppendLine(GenerateHeader(null));
		report.AppendLine(GenerateStyle(isDarkTheme));
		report.AppendLine(GenerateCombinedMetaInfo(surveys));
		
		// Generate AI summary for combined reports if Claude is available
		if (_claudeService != null)
		{
			try
			{
				// We'll generate a summary for each survey and combine them
				var summaries = new List<string>();
				for (var i = 0; i < surveys.Count; i++)
				{
					var summary = await _claudeService.GenerateSurveySummaryAsync(surveys[i], allResponses[i]);
					if (!string.IsNullOrEmpty(summary))
						summaries.Add(summary);
				}
				
				if (summaries.Count > 0)
					report.AppendLine(GenerateCombinedAISummary(summaries));
			}
			catch
			{
				// If AI summary generation fails, we just skip it and continue with the rest of the report
			}
		}
		
		report.AppendLine(await GenerateCombinedQuestions(surveys, allResponses));
		report.AppendLine("</body></html>");

		return report.ToString();
	}

	private static string GenerateHeader(Survey survey)
	{
		var report = new StringBuilder();
		var title = survey != null ? "Survey Report" : "Combined Survey Report";
		
		report.AppendLine($@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title}</title>
    <script src=""https://cdnjs.cloudflare.com/ajax/libs/Chart.js/4.4.1/chart.umd.min.js""></script>
</head>");

		return report.ToString();
	}

	private static string GenerateStyle(bool isDarkTheme = false)
	{
		var report = new StringBuilder();
		if (isDarkTheme)
		{
			report.AppendLine(@"<style>
		    :root {
		        --glass-card-bg-gradient-start: rgba(9, 106, 242, 0.3);
		        --glass-card-bg-gradient-end: rgba(9, 106, 242, 0.2);
		        --glass-card-box-shadow: rgba(9, 106, 242, 0.3);
		        --glass-card-border: rgba(255,255,255,0.25);
		        --glass-content-bg: rgba(255,255,255,0.08);
		    }
		    
		    body { 
		        font-family: Arial, sans-serif; 
		        line-height: 1.6;
		        max-width: 1200px;
		        margin: 0 auto;
		        padding: 20px;
		        color: #e1e1e1;
		        background-color: #121212;
		    }
		    .header {
		        text-align: center;
		        margin-bottom: 30px;
		        border-bottom: 2px solid #333;
		        padding-bottom: 20px;
		    }
		    .meta-info {
		        background: linear-gradient(135deg, var(--glass-card-bg-gradient-start) 0%, var(--glass-card-bg-gradient-end) 100%);
		        backdrop-filter: blur(10px);
		        border: 2px solid var(--glass-card-border);
		        box-shadow: 0 8px 32px var(--glass-card-box-shadow);
		        padding: 20px;
		        border-radius: 8px;
		        margin-bottom: 30px;
		    }
		    .question {
		        margin-bottom: 40px;
		        background: linear-gradient(135deg, var(--glass-card-bg-gradient-start) 0%, var(--glass-card-bg-gradient-end) 100%);
		        backdrop-filter: blur(10px);
		        border: 2px solid var(--glass-card-border);
		        box-shadow: 0 8px 32px var(--glass-card-box-shadow);
		        padding: 20px;
		        border-radius: 8px;
		    }
		    .question-header {
		        font-size: 1.2em;
		        font-weight: bold;
		        margin-bottom: 15px;
		        color: #e1e1e1;
		        border-bottom: 2px solid #444;
		        padding-bottom: 10px;
		    }
		    .response-summary {
		        margin: 20px 0;
		    }
		    .stats-container {
		        display: flex;
		        gap: 30px;
		        margin-bottom: 20px;
		    }
		    .stats-box {
		        flex: 1;
		        background: var(--glass-content-bg);
		        backdrop-filter: blur(8px);
		        border: 1px solid var(--glass-card-border);
		        padding: 15px;
		        border-radius: 6px;
		    }
		    .chart-wrapper {
		        height: 300px;
		        margin-top: 15px;
		        position: relative;
		    }
		    .text-response {
		        background: var(--glass-content-bg);
		        backdrop-filter: blur(8px);
		        border: 1px solid var(--glass-card-border);
		        padding: 15px;
		        border-radius: 6px;
		        margin: 10px 0;
		    }
		    table {
		        width: 100%;
		        border-collapse: collapse;
		        margin-top: 15px;
		    }
		    th, td {
		        padding: 12px;
		        border: 1px solid #444;
		        text-align: left;
		    }
		    th {
		        background-color: rgba(9, 106, 242, 0.25);
		        font-weight: bold;
		    }
		    tr:hover {
		        background-color: rgba(9, 106, 242, 0.15);
		    }
</style>
<body>");
			return report.ToString();
		}
		
		// Light theme
		report.AppendLine(@"<style>
    :root {
        --glass-card-bg-gradient-start: rgba(9, 106, 242, 0.3);
        --glass-card-bg-gradient-end: rgba(9, 106, 242, 0.2);
        --glass-card-box-shadow: rgba(9, 106, 242, 0.3);
        --glass-card-border: rgba(255,255,255,0.25);
        --glass-content-bg: rgba(255,255,255,0.08);
    }
    
    body { 
        font-family: Arial, sans-serif; 
        line-height: 1.6;
        max-width: 1200px;
        margin: 0 auto;
        padding: 20px;
        color: #333;
    }
    .header {
        text-align: center;
        margin-bottom: 30px;
        border-bottom: 2px solid #eee;
        padding-bottom: 20px;
    }
    .meta-info {
        background: linear-gradient(135deg, var(--glass-card-bg-gradient-start) 0%, var(--glass-card-bg-gradient-end) 100%);
        backdrop-filter: blur(10px);
        border: 2px solid var(--glass-card-border);
        box-shadow: 0 8px 32px var(--glass-card-box-shadow);
        padding: 20px;
        border-radius: 8px;
        margin-bottom: 30px;
        color: #1a1a1a;
    }
    .question {
        margin-bottom: 40px;
        background: linear-gradient(135deg, var(--glass-card-bg-gradient-start) 0%, var(--glass-card-bg-gradient-end) 100%);
        backdrop-filter: blur(10px);
        border: 2px solid var(--glass-card-border);
        box-shadow: 0 8px 32px var(--glass-card-box-shadow);
        padding: 20px;
        border-radius: 8px;
        color: #1a1a1a;
    }
    .question-header {
        font-size: 1.2em;
        font-weight: bold;
        margin-bottom: 15px;
        color: #1a1a1a;
        border-bottom: 2px solid rgba(0,0,0,0.3);
        padding-bottom: 10px;
    }
    .response-summary {
        margin: 20px 0;
    }
    .stats-container {
        display: flex;
        gap: 30px;
        margin-bottom: 20px;
    }
    .stats-box {
        flex: 1;
        background: var(--glass-content-bg);
        backdrop-filter: blur(8px);
        border: 1px solid var(--glass-card-border);
        padding: 15px;
        border-radius: 6px;
    }
    .chart-wrapper {
        height: 300px;
        margin-top: 15px;
        position: relative;
    }
    .text-response {
        background: var(--glass-content-bg);
        backdrop-filter: blur(8px);
        border: 1px solid var(--glass-card-border);
        padding: 15px;
        border-radius: 6px;
        margin: 10px 0;
    }
    table {
        width: 100%;
        border-collapse: collapse;
        margin-top: 15px;
    }
    th, td {
        padding: 12px;
        border: 1px solid rgba(0,0,0,0.2);
        text-align: left;
    }
    th {
        background-color: rgba(9, 106, 242, 0.15);
        font-weight: bold;
    }
    tr:hover {
        background-color: rgba(9, 106, 242, 0.1);
    }
    strong {
        color: #096AF2;
    }
    h4 {
        color: #096AF2;
    }
</style>
<body>");

		return report.ToString();
	}

	private static string GenerateMetaInfo(Survey survey)
	{
		var report = new StringBuilder();
		var responseRate = survey.Participants.Count > 0
			? (survey.UsedCompletionCodes * 100.0 / survey.Participants.Count).ToString("F1")
			: "0.0";

		report.AppendLine($@"
<div class=""header"">
    <h1>{HttpUtility.HtmlEncode(survey.Title)}</h1>
    <p>{HttpUtility.HtmlEncode(survey.Description)}</p>
</div>
<div class=""meta-info"">
    <p><strong>Survey Manager:</strong> {HttpUtility.HtmlEncode(survey.Manager?.DisplayName ?? "N/A")}</p>
    <p><strong>Creation Date:</strong> {survey.CreationDate:d}</p>
    <p><strong>Participation Type:</strong> {survey.ParticipationType}</p>
    <p><strong>Total Participants:</strong> {survey.Participants.Count}</p>
    <p><strong>Response Rate:</strong> {responseRate}%</p>
</div>");

		return report.ToString();
	}

	private static async Task<string> GenerateQuestions(Survey survey, List<Response> responses)
	{
		var report = new StringBuilder();
		report.AppendLine("<div class=\"questions\">");

		foreach (var question in survey.Questions)
		{
			var questionResponses = responses.Where(r => r.QuestionId == question.Id).ToList();
			var chartId = $"chart_{question.Id}";

			report.AppendLine($@"
<div class=""question"">
    <div class=""question-header"">Q: {HttpUtility.HtmlEncode(question.Text)}</div>");

			switch (question.Type)
			{
				case QuestionTypes.Rating:
					report.AppendLine(GenerateRatingQuestion(questionResponses, chartId));
					break;

				case QuestionTypes.Text:
					report.AppendLine(GenerateTextQuestion(questionResponses));
					break;

				case QuestionTypes.SingleChoice:
				case QuestionTypes.MultipleChoice:
					report.AppendLine(GenerateChoiceQuestion(questionResponses, chartId));
					break;
			}

			report.AppendLine("</div>");
		}

		report.AppendLine("</div>");
		return report.ToString();
	}

	private static string GenerateRatingQuestion(List<Response> responses, string chartId)
	{
		var report = new StringBuilder();
		var ratings = responses.Select(r => int.Parse(r.Answer)).ToList();
		var average = ratings.Any() ? ratings.Average() : 0;
		var distribution = Enumerable.Range(1, 5)
									 .ToDictionary(i => i,
												   i => ratings.Count(r => r == i));

		report.AppendLine($@"
    <div class=""response-summary"">
        <div class=""stats-container"">
            <div class=""stats-box"">
                <div style=""display: flex; justify-content: space-between; align-items: flex-start;"">
                    <div style=""flex: 0 0 60%;"">
                        <p><strong>Average Rating:</strong> {average:F2}</p>
                        <p><strong>Total Responses:</strong> {ratings.Count}</p>
                        <div class=""chart-wrapper"">
                            <canvas id=""{chartId}""></canvas>
                        </div>
                    </div>
                    <div style=""flex: 0 0 35%;"">
                        <table>
                            <tr>
                                <th>Rating</th>
                                <th>Count</th>
                                <th>Percentage</th>
                            </tr>");

		foreach (var (rating, count) in distribution)
		{
			var percentage = ratings.Any() ? count * 100.0 / ratings.Count : 0;
			report.AppendLine($@"
                            <tr>
                                <td>{rating}</td>
                                <td>{count}</td>
                                <td>{percentage:F1}%</td>
                            </tr>");
		}

		report.AppendLine($@"
                        </table>
                    </div>
                </div>
                <script>
                    new Chart(document.getElementById('{chartId}'), {{
                        type: 'pie',
                        data: {{
                            labels: [{string.Join(",", distribution.Keys.Select(k => $"'{k}'"))}],
                            datasets: [{{
                                data: [{string.Join(",", distribution.Values)}],
                                backgroundColor: [{CHART_COLORS}]
                            }}]
                        }},
                        options: {{
                            responsive: true,
                            maintainAspectRatio: false,
                            plugins: {{
                                legend: {{ position: 'bottom' }},
                                title: {{
                                    display: true,
                                    text: 'Rating Distribution'
                                }}
                            }}
                        }}
                    }});
                </script>
            </div>
        </div>
    </div>");

		return report.ToString();
	}

	private static string GenerateTextQuestion(List<Response> responses)
	{
		var report = new StringBuilder();
		report.AppendLine(@"<div class=""response-summary"">");

		foreach (var response in responses.Where(r => !string.IsNullOrWhiteSpace(r.Answer)))
			report.AppendLine($@"
    <div class=""text-response"">
        {HttpUtility.HtmlEncode(response.Answer)}
    </div>");

		report.AppendLine("</div>");
		return report.ToString();
	}

	private static string GenerateChoiceQuestion(List<Response> responses, string chartId)
	{
		var report = new StringBuilder();
		var choices = responses
					  .SelectMany(r => r.Answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					  .GroupBy(a => a)
					  .OrderByDescending(g => g.Count())
					  .ToDictionary(g => g.Key, g => g.Count());

		report.AppendLine($@"
    <div class=""response-summary"">
        <div class=""stats-container"">
            <div class=""stats-box"">
                <div style=""display: flex; justify-content: space-between; align-items: flex-start;"">
                    <div style=""flex: 0 0 60%;"">
                        <p><strong>Total Responses:</strong> {responses.Count}</p>
                        <div class=""chart-wrapper"">
                            <canvas id=""{chartId}""></canvas>
                        </div>
                    </div>
                    <div style=""flex: 0 0 35%;"">
                        <table>
                            <tr>
                                <th>Choice</th>
                                <th>Count</th>
                                <th>Percentage</th>
                            </tr>");

		foreach (var (choice, count) in choices)
		{
			var percentage = responses.Any() ? count * 100.0 / responses.Count : 0;
			report.AppendLine($@"
                            <tr>
                                <td>{HttpUtility.HtmlEncode(choice)}</td>
                                <td>{count}</td>
                                <td>{percentage:F1}%</td>
                            </tr>");
		}

		report.AppendLine($@"
                        </table>
                    </div>
                </div>
                <script>
                    new Chart(document.getElementById('{chartId}'), {{
                        type: 'pie',
                        data: {{
                            labels: [{string.Join(",", choices.Keys.Select(k => $"'{HttpUtility.HtmlEncode(k)}'"))}],
                            datasets: [{{
                                data: [{string.Join(",", choices.Values)}],
                                backgroundColor: [{CHART_COLORS}]
                            }}]
                        }},
                        options: {{
                            responsive: true,
                            maintainAspectRatio: false,
                            plugins: {{
                                legend: {{ position: 'bottom' }},
                                title: {{
                                    display: true,
                                    text: 'Response Distribution'
                                }}
                            }}
                        }}
                    }});
                </script>
            </div>
        </div>
    </div>");

		return report.ToString();
	}

	private string GenerateAISummary(string summary)
	{
		if (string.IsNullOrEmpty(summary))
			return string.Empty;

		var report = new StringBuilder();
		report.AppendLine($@"
<div class=""question"">
    <div class=""question-header"" style=""display: flex; align-items: center;"">
        <div>AI-Powered Survey Summary</div>
        <div style=""margin-left: auto; font-size: 0.8em; color: #555; font-style: italic;"">
            Generated by Claude AI
        </div>
    </div>
    <div class=""text-response"" style=""white-space: pre-line;"">{summary}</div>
</div>");

		return report.ToString();
	}

	private string GenerateCombinedAISummary(List<string> summaries)
	{
		if (summaries == null || !summaries.Any())
			return string.Empty;

		var report = new StringBuilder();
		report.AppendLine($@"
<div class=""question"">
    <div class=""question-header"" style=""display: flex; align-items: center;"">
        <div>AI-Powered Survey Summaries</div>
        <div style=""margin-left: auto; font-size: 0.8em; color: #555; font-style: italic;"">
            Generated by Claude AI
        </div>
    </div>");

		foreach (var summary in summaries)
			report.AppendLine($@"<div class=""text-response"" style=""margin-bottom: 15px;"">{summary}</div>");

		report.AppendLine("</div>");
		return report.ToString();
	}

	private static string GenerateCombinedMetaInfo(List<Survey> surveys)
	{
		var report = new StringBuilder();
		var totalParticipants = surveys.Sum(s => s.Participants.Count);
		var totalResponses = surveys.Sum(s => s.UsedCompletionCodes);
		var responseRate = totalParticipants > 0
			? (totalResponses * 100.0 / totalParticipants).ToString("F1")
			: "0.0";

		report.AppendLine($@"
<div class=""header"">
    <h1>Combined Survey Report</h1>
    <p>Aggregated data from {surveys.Count} surveys</p>
</div>
<div class=""meta-info"">
    <p><strong>Number of Surveys:</strong> {surveys.Count}</p>
    <p><strong>Survey Titles:</strong> {string.Join(", ", surveys.Select(s => HttpUtility.HtmlEncode(s.Title)))}</p>
    <p><strong>Date Range:</strong> {surveys.Min(s => s.CreationDate):d} - {surveys.Max(s => s.ScheduledEndDate ?? DateTime.Now):d}</p>
    <p><strong>Total Participants:</strong> {totalParticipants}</p>
    <p><strong>Total Responses:</strong> {totalResponses}</p>
    <p><strong>Overall Response Rate:</strong> {responseRate}%</p>
</div>");

		return report.ToString();
	}

	private async Task<string> GenerateCombinedQuestions(List<Survey> surveys, List<List<Response>> allResponses)
	{
		var report = new StringBuilder();
		report.AppendLine("<div class=\"questions\">");

		// Group questions by text to combine similar questions across surveys
		var questionGroups = new Dictionary<string, List<(Question Question, List<Response> Responses, Survey Survey)>>();
		
		for (var i = 0; i < surveys.Count; i++)
		{
			var survey = surveys[i];
			var responses = allResponses[i];
			
			foreach (var question in survey.Questions)
			{
				var questionResponses = responses.Where(r => r.QuestionId == question.Id).ToList();
				
				if (!questionGroups.ContainsKey(question.Text))
					questionGroups[question.Text] = new List<(Question, List<Response>, Survey)>();
					
				questionGroups[question.Text].Add((question, questionResponses, survey));
			}
		}
		
		// Generate report for each unique question
		var questionCounter = 0;
		foreach (var group in questionGroups)
		{
			questionCounter++;
			var combinedChartId = $"combined_chart_{questionCounter}";
			var questionText = group.Key;
			var questionGroupItems = group.Value;
			
			// We'll only consider questions of the same type for combining
			var firstItem = questionGroupItems.First();
			var questionType = firstItem.Question.Type;
			var similarQuestions = questionGroupItems.Where(q => q.Question.Type == questionType).ToList();
			
			if (similarQuestions.Count == 0)
				continue;
				
			report.AppendLine($@"
<div class=""question"">
    <div class=""question-header"">Q: {HttpUtility.HtmlEncode(questionText)}</div>
    <p><em>Data combined from {similarQuestions.Count} surveys</em></p>");

			switch (questionType)
			{
				case QuestionTypes.Rating:
					report.AppendLine(GenerateCombinedRatingQuestion(similarQuestions, combinedChartId));
					break;

				case QuestionTypes.Text:
					report.AppendLine(GenerateCombinedTextQuestion(similarQuestions));
					break;

				case QuestionTypes.SingleChoice:
				case QuestionTypes.MultipleChoice:
					report.AppendLine(GenerateCombinedChoiceQuestion(similarQuestions, combinedChartId));
					break;
			}

			report.AppendLine("</div>");
		}

		report.AppendLine("</div>");
		return report.ToString();
	}
	
	private string GenerateCombinedRatingQuestion(List<(Question Question, List<Response> Responses, Survey Survey)> items, string chartId)
	{
		var report = new StringBuilder();
		var allRatings = items.SelectMany(i => i.Responses.Select(r => int.Parse(r.Answer))).ToList();
		var average = allRatings.Count > 0 ? allRatings.Average() : 0;
		var distribution = Enumerable.Range(1, 5)
									 .ToDictionary(i => i,
												   i => allRatings.Count(r => r == i));

		report.AppendLine($@"
    <div class=""response-summary"">
        <div class=""stats-container"">
            <div class=""stats-box"">
                <div style=""display: flex; justify-content: space-between; align-items: flex-start;"">
                    <div style=""flex: 0 0 60%;"">
                        <p><strong>Average Rating:</strong> {average:F2}</p>
                        <p><strong>Total Responses:</strong> {allRatings.Count}</p>
                        <div class=""chart-wrapper"">
                            <canvas id=""{chartId}""></canvas>
                        </div>
                    </div>
                    <div style=""flex: 0 0 35%;"">
                        <table>
                            <tr>
                                <th>Rating</th>
                                <th>Count</th>
                                <th>Percentage</th>
                            </tr>");

		foreach (var (rating, count) in distribution)
		{
			var percentage = allRatings.Any() ? count * 100.0 / allRatings.Count : 0;
			report.AppendLine($@"
                            <tr>
                                <td>{rating}</td>
                                <td>{count}</td>
                                <td>{percentage:F1}%</td>
                            </tr>");
		}

		report.AppendLine($@"
                        </table>
                    </div>
                </div>
                <script>
                    new Chart(document.getElementById('{chartId}'), {{
                        type: 'pie',
                        data: {{
                            labels: [{string.Join(",", distribution.Keys.Select(k => $"'{k}'"))}],
                            datasets: [{{
                                data: [{string.Join(",", distribution.Values)}],
                                backgroundColor: [{CHART_COLORS}]
                            }}]
                        }},
                        options: {{
                            responsive: true,
                            maintainAspectRatio: false,
                            plugins: {{
                                legend: {{ position: 'bottom' }},
                                title: {{
                                    display: true,
                                    text: 'Combined Rating Distribution'
                                }}
                            }}
                        }}
                    }});
                </script>
            </div>
        </div>
        
        <div class=""stats-box"" style=""margin-top: 20px;"">
            <h4>Breakdown by Survey:</h4>
            <table>
                <tr>
                    <th>Survey</th>
                    <th>Average Rating</th>
                    <th>Responses</th>
                </tr>");
                
		foreach (var (question, responses, survey) in items)
		{
			var ratings = responses.Select(r => int.Parse(r.Answer)).ToList();
			var surveyAvg = ratings.Any() ? ratings.Average() : 0;
			
			report.AppendLine($@"
                <tr>
                    <td>{HttpUtility.HtmlEncode(survey.Title)}</td>
                    <td>{surveyAvg:F2}</td>
                    <td>{ratings.Count}</td>
                </tr>");
		}
                
		report.AppendLine(@"
            </table>
        </div>
    </div>");

		return report.ToString();
	}
	
	private static string GenerateCombinedTextQuestion(List<(Question Question, List<Response> Responses, Survey Survey)> items)
	{
		var report = new StringBuilder();
		report.AppendLine(@"<div class=""response-summary"">");

		foreach (var (question, responses, survey) in items)
		{
			if (responses.All(r => string.IsNullOrWhiteSpace(r.Answer)))
				continue;
				
			report.AppendLine($@"<h4>{HttpUtility.HtmlEncode(survey.Title)}</h4>");
				
			foreach (var response in responses.Where(r => !string.IsNullOrWhiteSpace(r.Answer)))
				report.AppendLine($@"
    <div class=""text-response"">
        {HttpUtility.HtmlEncode(response.Answer)}
    </div>");
		}

		report.AppendLine("</div>");
		return report.ToString();
	}
	
	private static string GenerateCombinedChoiceQuestion(List<(Question Question, List<Response> Responses, Survey Survey)> items, string chartId)
	{
		var report = new StringBuilder();
		
		// Combine all choices from all surveys
		var allChoices = items
					  .SelectMany(i => i.Responses
					  .SelectMany(r => r.Answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
					  .GroupBy(a => a)
					  .OrderByDescending(g => g.Count())
					  .ToDictionary(g => g.Key, g => g.Count());
					  
		var totalResponses = items.Sum(i => i.Responses.Count);

		report.AppendLine($@"
    <div class=""response-summary"">
        <div class=""stats-container"">
            <div class=""stats-box"">
                <div style=""display: flex; justify-content: space-between; align-items: flex-start;"">
                    <div style=""flex: 0 0 60%;"">
                        <p><strong>Total Responses:</strong> {totalResponses}</p>
                        <div class=""chart-wrapper"">
                            <canvas id=""{chartId}""></canvas>
                        </div>
                    </div>
                    <div style=""flex: 0 0 35%;"">
                        <table>
                            <tr>
                                <th>Choice</th>
                                <th>Count</th>
                                <th>Percentage</th>
                            </tr>");

		foreach (var (choice, count) in allChoices)
		{
			var percentage = totalResponses > 0 ? count * 100.0 / totalResponses : 0;
			report.AppendLine($@"
                            <tr>
                                <td>{HttpUtility.HtmlEncode(choice)}</td>
                                <td>{count}</td>
                                <td>{percentage:F1}%</td>
                            </tr>");
		}

		report.AppendLine($@"
                        </table>
                    </div>
                </div>
                <script>
                    new Chart(document.getElementById('{chartId}'), {{
                        type: 'pie',
                        data: {{
                            labels: [{string.Join(",", allChoices.Keys.Select(k => $"'{HttpUtility.HtmlEncode(k)}'"))}],
                            datasets: [{{
                                data: [{string.Join(",", allChoices.Values)}],
                                backgroundColor: [{CHART_COLORS}]
                            }}]
                        }},
                        options: {{
                            responsive: true,
                            maintainAspectRatio: false,
                            plugins: {{
                                legend: {{ position: 'bottom' }},
                                title: {{
                                    display: true,
                                    text: 'Combined Response Distribution'
                                }}
                            }}
                        }}
                    }});
                </script>
            </div>
        </div>
        
        <div class=""stats-box"" style=""margin-top: 20px;"">
            <h4>Breakdown by Survey:</h4>
            <table>
                <tr>
                    <th>Survey</th>
                    <th>Responses</th>
                    <th>Top Choice</th>
                </tr>");
                
		foreach (var (question, responses, survey) in items)
		{
			if (responses.Count == 0)
				continue;
				
			var surveyChoices = responses
					.SelectMany(r => r.Answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
					.GroupBy(a => a)
					.OrderByDescending(g => g.Count())
					.ToDictionary(g => g.Key, g => g.Count());
			
			var topChoice = surveyChoices.Any() ? surveyChoices.First().Key : "None";
			
			report.AppendLine($@"
                <tr>
                    <td>{HttpUtility.HtmlEncode(survey.Title)}</td>
                    <td>{responses.Count}</td>
                    <td>{HttpUtility.HtmlEncode(topChoice)}</td>
                </tr>");
		}
                
		report.AppendLine(@"
            </table>
        </div>
    </div>");

		return report.ToString();
	}

	public async Task<string> GenerateCsvAsync(Survey survey, List<Response> responses)
	{
		var csv = new StringBuilder();
		csv.AppendLine("Question,Type,Response,Rating Discrepancy");

		foreach (var question in survey.Questions)
		{
			var questionResponses = responses.Where(r => r.QuestionId == question.Id);

			foreach (var response in questionResponses)
			{
				if (question.Type == QuestionTypes.Text && string.IsNullOrWhiteSpace(response.Answer))
					continue;

				csv.AppendLine($"\"{question.Text.Replace("\"", "\"\"")}\",{question.Type},\"{response.Answer.Replace("\"", "\"\"")}\",{response.Discrepancy}");
			}
		}

		return csv.ToString();
	}
	
	public async Task<string> GenerateCombinedCsvAsync(List<Survey> surveys, List<List<Response>> allResponses)
	{
		var csv = new StringBuilder();
		csv.AppendLine("Survey,Question,Type,Response,Rating Discrepancy");

		for (var i = 0; i < surveys.Count; i++)
		{
			var survey = surveys[i];
			var responses = allResponses[i];
			
			foreach (var question in survey.Questions)
			{
				var questionResponses = responses.Where(r => r.QuestionId == question.Id);

				foreach (var response in questionResponses)
				{
					if (question.Type == QuestionTypes.Text && string.IsNullOrWhiteSpace(response.Answer))
						continue;

					csv.AppendLine($"\"{survey.Title.Replace("\"", "\"\"")}\",\"{question.Text.Replace("\"", "\"\"")}\",{question.Type},\"{response.Answer.Replace("\"", "\"\"")}\",{response.Discrepancy}");
				}
			}
		}

		return csv.ToString();
	}
}