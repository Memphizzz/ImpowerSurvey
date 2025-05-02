using ImpowerSurvey.Components.Model;
using Microsoft.EntityFrameworkCore;

namespace ImpowerSurvey.Services;

public class SurveyCodeService(IDbContextFactory<SurveyDbContext> contextFactory, ILogService logService)
{
	// ReSharper disable ReplaceWithPrimaryConstructorParameter
	private readonly IDbContextFactory<SurveyDbContext> _contextFactory = contextFactory;
	private readonly ILogService _logService = logService;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter

	public async Task<DataServiceResult<Guid>> ValidateEntryCodeAsync(string code)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();

		var entryCode = await dbContext.EntryCodes
									   .Include(x => x.Survey)
									   .Where(x => x.Code == code && !x.IsUsed)
									   .FirstOrDefaultAsync();

		if (entryCode == null)
			return ServiceResult.Failure<Guid>(Constants.EntryCodes.InvalidOrUsed);

		switch (entryCode.Survey.State)
		{
			case SurveyStates.Created:
			case SurveyStates.Scheduled:
			case SurveyStates.Closed:
				return ServiceResult.Failure<Guid>(Constants.Survey.NoEntry);

			case SurveyStates.Running:
				return ServiceResult.Success(entryCode.SurveyId, Constants.EntryCodes.Valid);

			default:
				throw new ArgumentOutOfRangeException();
		}
	}

	public async Task<bool> BurnEntryCodeAsync(string code)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();

		var entryCode = await dbContext.EntryCodes
									   .Where(x => x.Code == code && x.IsIssued && !x.IsUsed)
									   .FirstOrDefaultAsync();

		if (entryCode == null)
			return false;

		// Allow Demos by not marking this specific code as used
		if(string.Equals(code, "DEMO", StringComparison.OrdinalIgnoreCase))
			return true;

		entryCode.IsUsed = true;
		await dbContext.SaveChangesAsync();
		return true;
	}

	public async Task<DataServiceResult<string>> GetCompletionCodeAsync(Guid surveyId)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();

		try
		{
			var unusedCodes = await dbContext.CompletionCodes
											 .Where(x => x.SurveyId == surveyId && !x.IsUsed)
											 .ToListAsync();

			if (unusedCodes.Count == 0)
				return ServiceResult.Failure<string>(Constants.CompletionCodes.NoneAvailable);

			var randomIndex = new Random().Next(0, unusedCodes.Count);
			var selectedCode = unusedCodes[randomIndex];
			return ServiceResult.Success(selectedCode.Code, string.Empty);
		}
		catch (Exception)
		{
			return ServiceResult.Failure<string>(Constants.CompletionCodes.Error);
		}
	}

	public async Task<ServiceResult> ValidateAndBurnCompletionCodeAsync(Guid surveyId, string code, string usedBy)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		var strategy = dbContext.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync();

			try
			{
				var completionCode = await dbContext.CompletionCodes
													.AsSplitQuery()
													.Where(x => x.SurveyId == surveyId && x.Code == code && !x.IsUsed)
													.Include(x => x.Survey)
													.FirstOrDefaultAsync();

				if (completionCode == null)
					return ServiceResult.Failure(Constants.CompletionCodes.Invalid);

				completionCode.IsUsed = true;
				if (string.IsNullOrWhiteSpace(usedBy))
					usedBy = Constants.Survey.Anonymous;

				var completionTracking = new ParticipationRecord
				{
					CompletionCodeId = completionCode.Id,
					UsedBy = usedBy,
					UsedAt = DateTime.UtcNow
				};

				if (completionCode.Survey.ParticipationType == ParticipationTypes.Manual)
					completionCode.Survey.Participants.Add(usedBy);

				dbContext.ParticipationRecords.Add(completionTracking);
				await dbContext.SaveChangesAsync();

				await transaction.CommitAsync();
				return ServiceResult.Success(Constants.CompletionCodes.Burned);
			}
			catch (Exception e)
			{
				await transaction.RollbackAsync();
				return ServiceResult.Failure(e.Message);
			}
		});
	}

	public async Task<EntryCode> GetEntryCode(Guid surveyId, bool markIssued)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();

		var code = await dbContext.EntryCodes
								  .Where(x => x.SurveyId == surveyId && !x.IsUsed && !x.IsIssued)
								  .OrderBy(x => Guid.NewGuid())
								  .FirstOrDefaultAsync();

		if (markIssued && code != null)
		{
			code.IsIssued = true;
			await dbContext.SaveChangesAsync();
			
			// Check if remaining entry codes are below threshold and top up if needed
			var remainingCount = await dbContext.EntryCodes.CountAsync(x => x.SurveyId == surveyId && !x.IsUsed && !x.IsIssued);
			
			if (remainingCount < 5)
			{
				// Fire and forget to avoid blocking the current operation
				_ = TopUpEntryCodesAsync(surveyId);
			}
		}

		return code;
	}

	public async Task<bool> MarkEntryCodeIssued(Guid entryCodeId)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		var code = await dbContext.EntryCodes.FindAsync(entryCodeId);

		if (code != null)
		{
			code.IsIssued = true;
			await dbContext.SaveChangesAsync();
			return true;
		}

		return false;
	}
	
	/// <summary>
	/// Generates additional entry codes for a survey when the remaining count is low.
	/// This maintains SHIELD compliance by generating codes in batches rather than individually,
	/// preventing timing correlations between user requests and code generation.
	/// </summary>
	/// <param name="surveyId">The ID of the survey to generate codes for</param>
	/// <returns>The number of new codes generated</returns>
	public async Task<int> TopUpEntryCodesAsync(Guid surveyId)
	{
		try
		{
			await using var dbContext = await _contextFactory.CreateDbContextAsync();
			
			// Get the survey to validate it exists and check its state
			var survey = await dbContext.Surveys.FindAsync(surveyId);
			if (survey is not { State: SurveyStates.Running })
			{
				await _logService.LogAsync(LogSource.SurveyCodeService, LogLevel.Warning, 
					$"Attempted to top up entry codes for invalid survey. ID: {surveyId}, State: {survey?.State}");
				return 0;
			}
			
			// Generate a small batch of new codes (10)
			// Using a batch prevents temporal correlation with individual requests
			const int batchSize = 10;
			
			for (var i = 0; i < batchSize; i++)
				dbContext.EntryCodes.Add(EntryCode.Create(surveyId));

			await dbContext.SaveChangesAsync();
			
			await _logService.LogAsync(LogSource.SurveyCodeService, LogLevel.Information,
				$"Generated {batchSize} additional entry codes for survey ID: {surveyId}");
				
			return batchSize;
		}
		catch (Exception ex)
		{
			await _logService.LogAsync(LogSource.SurveyCodeService, LogLevel.Error,
				$"Error generating additional entry codes for survey ID: {surveyId}. Error: {ex.Message}");
			return 0;
		}
	}
	
	/// <summary>
	/// Creates a special "demo" entry code for example surveys.
	/// Ensures only one active demo code exists across all surveys by marking any other demo codes as used.
	/// </summary>
	/// <param name="surveyId">The ID of the survey to create the demo code for</param>
	/// <returns>The created entry code or null if there was an error</returns>
	public async Task<EntryCode> CreateDemoEntryCodeAsync(Guid surveyId)
	{
		await using var dbContext = await _contextFactory.CreateDbContextAsync();
		var strategy = dbContext.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync();

			try
			{
				// Check if a demo code already exists for this survey
				var existingDemoCode = await dbContext.EntryCodes
					.FirstOrDefaultAsync(x => x.SurveyId == surveyId && x.Code.ToLower() == "demo");
					
				if (existingDemoCode != null)
					return existingDemoCode;
				
				// Find any other demo codes from other surveys and mark them as used
				var otherDemoCodes = await dbContext.EntryCodes
					.Where(x => x.SurveyId != surveyId && x.Code.ToLower() == "demo" && !x.IsUsed)
					.ToListAsync();
				
				if (otherDemoCodes.Count > 0)
				{
					dbContext.EntryCodes.RemoveRange(otherDemoCodes);
					await dbContext.SaveChangesAsync();
					foreach (var otherCode in otherDemoCodes)
						await _logService.LogAsync(LogSource.SurveyCodeService, LogLevel.Information,
												   $"Removed demo entry code {otherCode.SurveyId} for survey {otherCode.SurveyId} since a new example survey is being created");
				}
					
				// Create the demo code
				var demoCode = EntryCode.Create(surveyId);
				demoCode.Code = "demo";
				demoCode.IsIssued = true; // Mark as issued so it doesn't get handed out by GetEntryCode
				
				dbContext.EntryCodes.Add(demoCode);
				await dbContext.SaveChangesAsync();
				
				await _logService.LogAsync(LogSource.SurveyCodeService, LogLevel.Information,
					$"Created demo entry code for survey ID: {surveyId}");
				
				await transaction.CommitAsync();	
				return demoCode;
			}
			catch (Exception ex)
			{
				await transaction.RollbackAsync();
				await _logService.LogAsync(LogSource.SurveyCodeService, LogLevel.Error,
										   $"Error creating demo entry code for survey ID: {surveyId}. Error: {ex.Message}");
				return null;
			}
		});
	}
}