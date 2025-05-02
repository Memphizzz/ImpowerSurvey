using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using Microsoft.AspNetCore.Components;

namespace ImpowerSurvey.Components.Controls;

public partial class ParticipationDialog
{
	[Parameter]
	public Guid SurveyId { get; set; }

	private Survey Survey { get; set; }
	public string ParticipantID { get; set; }
	private List<ParticipantsListItem> ParticipantsList { get; set; } = [];
	private List<ChartItem> ChartData { get; set; } = [];
	private StatsModel Stats { get; set; } = new();

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
			await LoadSurveyData();
	}

	private async Task LoadSurveyData()
	{
		Survey = await SurveyService.GetSurveyByIdAsync(SurveyId);

		if (Survey == null)
			throw new ArgumentException("Survey not found", nameof(SurveyId));

		var particpantsResult = await SurveyService.GetSurveyParticipationStatsAsync(SurveyId);

		if (particpantsResult.Successful)
		{
			var stats = particpantsResult.Data;
			// For closed surveys, use the stored statistics; For active surveys, get live data
			var createdCompletionCodes = Survey.State == SurveyStates.Closed ? Survey.CreatedCompletionCodesCount : Survey.CompletionCodes.Count;
			ParticipantsList = Survey.Participants.Select(x => new ParticipantsListItem(x, stats.ParticipationRecords.Any(ct => ct.UsedBy == x))).ToList();

			UpdateStats(stats.IssuedEntryCodes, stats.UsedEntryCodes, createdCompletionCodes, stats.SubmittedCompletionCodes);
			StateHasChanged();

		}
		else
			NotificationService.NotifyFromServiceResult(particpantsResult);

	}

	private void UpdateStats(int issuedEntryCodes, int usedEntryCodes, int createdCompletionCodes, int submittedCompletionCodes)
	{
		var entryCodeUsagePercentage = issuedEntryCodes > 0 
			? (double)usedEntryCodes / issuedEntryCodes * 100 
			: 0;
			
		var completionCodeSubmissionPercentage = issuedEntryCodes > 0 
			? (double)submittedCompletionCodes / issuedEntryCodes * 100
			: 0;

		ChartData =
		[
			new ChartItem { Title = "Completed", Value = submittedCompletionCodes },
			new ChartItem { Title = "Participation", Value = createdCompletionCodes - submittedCompletionCodes },
			new ChartItem { Title = "Discrepancy", Value = Math.Max(0, usedEntryCodes - createdCompletionCodes) }
		];

		Stats.UsedEntryCodes = usedEntryCodes.ToString();
		Stats.IssuedEntryCodes = issuedEntryCodes.ToString();
		Stats.SubmittedCompletionCodes = submittedCompletionCodes.ToString();
		Stats.EntryCodeUsagePercentage = $"{entryCodeUsagePercentage:F1}%";
		Stats.CompletionCodeSubmissionPercentage = $"{completionCodeSubmissionPercentage:F1}%";
	}

	public class ParticipantsListItem(string id, bool hasParticipated)
	{
		public string Id { get; set; } = id;
		public bool HasParticipated { get; set; } = hasParticipated;
	}

	public class ChartItem
	{
		public string Title { get; set; }
		public double Value { get; set; }
	}

	public class StatsModel
	{
		public string UsedEntryCodes { get; set; }
		public string IssuedEntryCodes { get; set; }
		public string SubmittedCompletionCodes { get; set; }
		public string EntryCodeUsagePercentage { get; set; }
		public string CompletionCodeSubmissionPercentage { get; set; }
	}
}