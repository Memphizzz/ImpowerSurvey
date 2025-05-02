using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using Microsoft.EntityFrameworkCore;

namespace ImpowerSurvey.Services;

/// <summary>
/// Service for managing surveys, including creation, updates, submissions, and results
/// </summary>
public class SurveyService
(
	IDbContextFactory<SurveyDbContext> contextFactory, UserService userService,
	DelayedSubmissionService delayedSubmissionService, SurveyCodeService surveyCodeService,
	ISlackService slackService, ILogService logService, ILeaderElectionService leaderElectionService,
	ISettingsService settingsService, IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
	/// <summary>
	/// Kicks off a survey by generating codes, setting state, and optionally sending invites
	/// </summary>
	/// <param name="surveyId">ID of the survey to kick off</param>
	/// <param name="model">Information needed to start the survey</param>
	/// <returns>A service result indicating success or failure</returns>
	public async Task<ServiceResult> KickOffSurvey(Guid surveyId, SurveyStartInfo model)
	{
		try
		{
			await using var dbContext = await contextFactory.CreateDbContextAsync();

			var survey = await dbContext.Surveys.FindAsync(surveyId);
			if (survey == null)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to kick off non-existent survey with ID: {surveyId}");
				return ServiceResult.Failure(Constants.Survey.NotFound);
			}

			if (survey.State != SurveyStates.Created)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to kick off survey in invalid state. Survey ID: {surveyId}, Current state: {survey.State}");
				return ServiceResult.Failure(Constants.Survey.NotInCreatedState);
			}

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Starting kick off process for survey: {survey.Title} (ID: {surveyId})");

			async Task GenerateCodesAsync(SurveyDbContext dbContext, int count)
			{
				// Get existing codes to avoid duplicates
				var existingEntryCodes = await dbContext.EntryCodes.Select(e => e.Code).ToHashSetAsync();
				var existingCompletionCodes = await dbContext.CompletionCodes.Select(c => c.Code).ToHashSetAsync();

				// Generate entry codes
				for (var i = 0; i < count * 3; i++)
				{
					var entryCode = EntryCode.Create(surveyId);

					while (existingEntryCodes.Contains(entryCode.Code))
						entryCode = EntryCode.Create(surveyId);

					if (existingEntryCodes.Add(entryCode.Code))
						dbContext.EntryCodes.Add(entryCode);
				}

				// Generate completion codes
				for (var i = 0; i < count * 1.5; i++)
				{
					var completionCode = CompletionCode.Create(surveyId);

					while (existingCompletionCodes.Contains(completionCode.Code))
						completionCode = CompletionCode.Create(surveyId);

					if (existingCompletionCodes.Add(completionCode.Code))
						dbContext.CompletionCodes.Add(completionCode);
				}
			}

			survey.ParticipationType = model.ParticipationType;
			switch (survey.ParticipationType)
			{
				case ParticipationTypes.Manual:
					await GenerateCodesAsync(dbContext, model.ParticipantCount);
					break;

				case ParticipationTypes.Slack:
					var manager = await userService.GetUserAsync(model.ManagerId);
					if (manager == null)
					{
						await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
												  $"Kick off failed - manager not found. Survey ID: {surveyId}, Manager ID: {model.ManagerId}");
						return ServiceResult.Failure(Constants.Survey.ManagerNotFound);
					}

					survey.Manager = manager;
					await GenerateCodesAsync(dbContext, model.ParticipantList.Count);
					break;

				default:
					var errorMsg = $"Invalid participation type: {model.ParticipationType} for survey ID: {surveyId}";
					await logService.LogAsync(LogSource.SurveyService, LogLevel.Error, errorMsg);
					throw new ArgumentOutOfRangeException(nameof(model.ParticipationType), model.ParticipationType, null);
			}

			await dbContext.SaveChangesAsync();

			var notificationErrors = string.Empty;
			if (model.HasEndDate)
				survey.ScheduledEndDate = model.EndDate;

			if (model.HasStartDate)
			{
				survey.State = SurveyStates.Scheduled;
				survey.ScheduledStartDate = model.StartDate;
				survey.Participants = model.ParticipantList;
			}
			else
			{
				if (survey.ParticipationType != ParticipationTypes.Manual)
				{
					var result = await SendInvites(surveyId, model.ParticipantList);
					if (!result.Successful)
					{
						notificationErrors = result.Message;
						await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
												  $"Failed to send invites for survey: {survey.Title} (ID: {surveyId}), Error: {notificationErrors}");
					}
				}

				survey.State = SurveyStates.Running;
			}

			await dbContext.SaveChangesAsync();
			var successMessage = survey.State == SurveyStates.Running ? Constants.Survey.KickOffRunning : $"{Constants.Survey.KickOffScheduled}{notificationErrors}";

			// Create a comprehensive summary of all operations performed
			var logDetails = new List<string>
			{
				$"State: {survey.State}", 
				$"ParticipationType: {survey.ParticipationType}"
			};

			switch (survey.ParticipationType)
			{
				case ParticipationTypes.Manual:
					logDetails.Add($"Generated codes for {model.ParticipantCount} manual participants");
					break;

				case ParticipationTypes.Slack:
					logDetails.Add($"Generated codes for {model.ParticipantList.Count} Slack participants");
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}

			if (model.HasEndDate)
				logDetails.Add($"End date: {model.EndDate}");

			if (model.HasStartDate)
				logDetails.Add($"Start date: {model.StartDate}");

			if (!string.IsNullOrEmpty(notificationErrors))
				logDetails.Add($"Notification errors: {notificationErrors}");

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Kick off completed for survey: {survey.Title} (ID: {surveyId}). {string.Join(" | ", logDetails)}");

			return ServiceResult.Success(successMessage);
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService,
							$"Error during kick off for survey ID: {surveyId}");
			return ServiceResult.Failure($"Failed to kick off survey: {ex.Message}");
		}
	}

	/// <summary>
	/// Sends invitations to participants for a survey
	/// </summary>
	/// <param name="surveyId">ID of the survey</param>
	/// <param name="participants">List of participant emails</param>
	/// <returns>A service result indicating success or failure</returns>
	public async Task<ServiceResult> SendInvites(Guid surveyId, List<string> participants)
	{
		try
		{
			await using var dbContext = await contextFactory.CreateDbContextAsync();
			var survey = await dbContext.Surveys.AsSplitQuery().Include(survey => survey.Manager).FirstOrDefaultAsync(x => x.Id == surveyId);

			if (survey == null)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to send invites for non-existent survey with ID: {surveyId}");
				return ServiceResult.Failure(Constants.Survey.NotFound);
			}

			if (survey.State != SurveyStates.Created && survey.State != SurveyStates.Running)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to send invites for survey in invalid state. Survey ID: {surveyId}, Current state: {survey.State}");
				return ServiceResult.Failure(Constants.Survey.ParticipantAddError);
			}

			participants = participants.Select(x => x.ToLower()).ToList();
			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Sending invites for survey: {survey.Title} (ID: {surveyId}), Participant count: {participants.Count}");

			switch (survey.ParticipationType)
			{
				case ParticipationTypes.Manual:
					await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
											  $"Cannot send invites for manual participation type. Survey ID: {surveyId}");
					return ServiceResult.Failure(Constants.Survey.InvalidParticipationType);

				case ParticipationTypes.Slack:
					var failures = new List<string>();
					var successCount = 0;

					foreach (var email in participants)
					{
						if (survey.Participants.Contains(email))
						{
							failures.Add($"{email}: {Constants.Survey.ParticipantAlreadyExists}");
							continue;
						}

						var entryCode = await surveyCodeService.GetEntryCode(survey.Id, false);
						if (entryCode == null)
						{
							failures.Add($"{email}: {Constants.EntryCodes.NoneAvailable}");
							continue;
						}

						var slackMessageSent = await slackService.SendSurveyInvitation(email, survey.Id, survey.Title, survey.Manager, entryCode.Code);
						if (!slackMessageSent)
						{
							failures.Add($"{email}: {Constants.Slack.Errors.SendFailed}");
							continue;
						}

						await surveyCodeService.MarkEntryCodeIssued(entryCode.Id);
						survey.Participants.Add(email);
						successCount++;
					}

					await dbContext.SaveChangesAsync();

					var message = failures.Count > 0 ? $"{Constants.EntryCodes.InvalidParticipants}: {string.Join(", ", failures)}" : string.Empty;

					var existingParticipants = failures.Count(f => f.Contains(Constants.Survey.ParticipantAlreadyExists));
					var noEntryCodes = failures.Count(f => f.Contains(Constants.EntryCodes.NoneAvailable));
					var slackFailures = failures.Count(f => f.Contains(Constants.Slack.Errors.SendFailed));

					var failureDetails = new List<string>();
					if (existingParticipants > 0)
						failureDetails.Add($"{existingParticipants} already existed");
					if (noEntryCodes > 0)
						failureDetails.Add($"{noEntryCodes} had no entry codes");
					if (slackFailures > 0)
						failureDetails.Add($"{slackFailures} Slack message failures");

					if (failures.Count < participants.Count)
					{
						await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
												  $"Completed sending invites for survey: {survey.Title} (ID: {surveyId}), Success: {successCount}, " +
												  $"Failures: {failures.Count}{(failureDetails.Count > 0 ? $" ({string.Join(", ", failureDetails)})" : "")}");
						return ServiceResult.Success(Constants.Survey.InvitesSent);
					}

					await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
											  $"Failed to send all invites for survey: {survey.Title} (ID: {surveyId}), " +
											  $"Failures: {failures.Count}{(failureDetails.Count > 0 ? $" ({string.Join(", ", failureDetails)})" : "")}");
					return ServiceResult.Failure(message);

				default:
					var errorMsg = $"Invalid participation type: {survey.ParticipationType} for survey ID: {surveyId}";
					await logService.LogAsync(LogSource.SurveyService, LogLevel.Error, errorMsg);
					throw new ArgumentOutOfRangeException(nameof(survey.ParticipationType), survey.ParticipationType, null);
			}
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService,
							$"Error sending invites for survey ID: {surveyId}");
			return ServiceResult.Failure($"Failed to send invites: {ex.Message}");
		}
	}

	/// <summary>
	/// Deletes a survey and all associated data including responses
	/// </summary>
	/// <param name="surveyId">ID of the survey to delete</param>
	/// <returns>A service result indicating success or failure</returns>
	public async Task<ServiceResult> DeleteSurvey(Guid surveyId)
	{
		await using var dbContext = await contextFactory.CreateDbContextAsync();
		var strategy = dbContext.Database.CreateExecutionStrategy();
		
		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await dbContext.Database.BeginTransactionAsync();

			try
			{
				var survey = await dbContext.Surveys.FindAsync(surveyId);
				if (survey == null)
				{
					await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
											  $"Attempt to delete non-existent survey with ID: {surveyId}");
					return ServiceResult.Failure(Constants.Survey.NotFound);
				}

				await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
										  $"Deleting survey: {survey.Title} (ID: {surveyId})");

				var responsesToDelete = await dbContext.Responses.Where(x => x.SurveyId == surveyId).ToListAsync();
				dbContext.Responses.RemoveRange(responsesToDelete);

				// Delete the survey
				dbContext.Surveys.Remove(survey);
				await dbContext.SaveChangesAsync();
				await transaction.CommitAsync();

				await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
										  $"Successfully deleted survey: {survey.Title} (ID: {surveyId}), deleted {responsesToDelete.Count} responses");

				return ServiceResult.Success(Constants.Survey.DeleteSuccess);
			}
			catch (Exception ex)
			{
				await transaction.RollbackAsync();
				ex.LogException(logService, LogSource.SurveyService, $"Error deleting survey ID: {surveyId}");
				return ServiceResult.Failure(Constants.Survey.DeleteError);
			}
		});
	}

	/// <summary>
	/// Closes a survey, flushes pending responses, and cleans up unused codes
	/// </summary>
	/// <param name="surveyId">ID of the survey to close</param>
	/// <returns>A service result indicating success or failure</returns>
	public async Task<ServiceResult> CloseSurvey(Guid surveyId)
	{
		try
		{
			// If this is not the leader, delegate the close operation to the leader
			if (!leaderElectionService.IsLeader)
			{
				var leaderId = await settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
				var leaderUrl = $"https://{leaderId}";
				var instanceSecret = configuration[Constants.App.EnvInstanceSecret] ??
									 Environment.GetEnvironmentVariable(Constants.App.EnvInstanceSecret, EnvironmentVariableTarget.Process);

				if (string.IsNullOrEmpty(instanceSecret))
				{
					await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
											  "Cannot close survey: instance secret not configured");
					return ServiceResult.Failure(Constants.Survey.CloseError);
				}

				try
				{
					// Request the leader to close the survey
					var closeResult = await httpClientFactory.CloseSurveyAsync(leaderUrl, leaderElectionService.InstanceId, surveyId, instanceSecret);

					return closeResult.Successful
						? ServiceResult.Success(Constants.Survey.CloseSuccess)
						: ServiceResult.Failure(closeResult.Message);
				}
				catch (Exception ex)
				{
					await logService.LogAsync(LogSource.SurveyService, LogLevel.Error,
											  $"Error requesting leader to close survey: {ex.Message}");
					return ServiceResult.Failure(Constants.Survey.CloseError);
				}
			}

			// Leader implementation - get survey from database
			await using var dbContext = await contextFactory.CreateDbContextAsync();
			var survey = await dbContext.Surveys.AsSplitQuery().Include(x => x.EntryCodes).Include(x => x.CompletionCodes).FirstOrDefaultAsync(x => x.Id == surveyId);
			if (survey == null)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to close non-existent survey with ID: {surveyId}");
				return ServiceResult.Failure(Constants.Survey.NotFound);
			}

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Closing survey: {survey.Title} (ID: {surveyId})");

			var flushResult = await delayedSubmissionService.FlushPendingResponses(survey.Id);
			var flushedCount = flushResult.Data;

			if (!flushResult.Successful)
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Failed to flush responses for survey {survey.Title}: {flushResult.Message}");

			// Change survey state
			survey.State = SurveyStates.Closed;

			// Store participation statistics before deleting unused codes
			survey.IssuedEntryCodesCount = survey.EntryCodes.Count(e => e.IsIssued);
			survey.UsedEntryCodesCount = survey.EntryCodes.Count(e => e.IsUsed);
			survey.CreatedCompletionCodesCount = survey.CompletionCodes.Count;
			survey.SubmittedCompletionCodesCount = survey.CompletionCodes.Count(c => c.IsUsed);

			// Remove unused codes
			var removedCompletionCodes = survey.CompletionCodes.RemoveAll(x => !x.IsUsed);
			var removedEntryCodes = survey.EntryCodes.RemoveAll(x => !x.IsIssued);

			var result = await dbContext.SaveChangesAsync() > 0;

			if (result)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
										  $"Successfully closed survey: {survey.Title} (ID: {surveyId}), " +
										  $"Flushed: {flushedCount} responses, " +
										  $"Stats - Issued: {survey.IssuedEntryCodesCount}, Used: {survey.UsedEntryCodesCount}, " +
										  $"Completion: {survey.CreatedCompletionCodesCount}, Submitted: {survey.SubmittedCompletionCodesCount}, " +
										  $"Removed: {removedCompletionCodes} completion codes, {removedEntryCodes} entry codes");
				return ServiceResult.Success(Constants.Survey.CloseSuccess);
			}

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
									  $"Failed to close survey: {survey.Title} (ID: {surveyId}) - No changes saved");
			return ServiceResult.Failure(Constants.Survey.CloseError);
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService, $"Error closing survey ID: {surveyId}");
			return ServiceResult.Failure(Constants.Survey.CloseError);
		}
	}

	/// <summary>
	/// Creates a duplicate of an existing survey with a new ID
	/// </summary>
	/// <param name="surveyId">ID of the survey to duplicate</param>
	/// <returns>A service result indicating success or failure</returns>
	public async Task<ServiceResult> DuplicateSurvey(Guid surveyId)
	{
		try
		{
			await using var dbContext = await contextFactory.CreateDbContextAsync();
			var originalSurvey = await dbContext.Surveys
												.AsSplitQuery()
												.Include(s => s.Questions)
												.ThenInclude(q => q.Options)
												.FirstOrDefaultAsync(s => s.Id == surveyId);

			if (originalSurvey == null)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to duplicate non-existent survey with ID: {surveyId}");
				return ServiceResult.Failure(Constants.Survey.NotFound);
			}

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Duplicating survey: {originalSurvey.Title} (ID: {surveyId})");

			var newSurvey = new Survey
			{
				Title = $"Copy of {originalSurvey.Title}",
				ManagerId = originalSurvey.ManagerId,
				Description = originalSurvey.Description,
				State = SurveyStates.Created,
				CreationDate = DateTime.UtcNow
			};

			var questionCount = 0;
			var optionCount = 0;

			foreach (var originalQuestion in originalSurvey.Questions)
			{
				var options = originalQuestion.Options.Select(o => new QuestionOption { Text = o.Text }).ToList();
				optionCount += options.Count;

				newSurvey.Questions.Add(new Question
				{
					Text = originalQuestion.Text,
					Type = originalQuestion.Type,
					Options = options
				});

				questionCount++;
			}

			dbContext.Surveys.Add(newSurvey);
			await dbContext.SaveChangesAsync();

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Successfully duplicated survey: {originalSurvey.Title} (ID: {surveyId}) to new survey ID: {newSurvey.Id}, " +
									  $"with {questionCount} questions and {optionCount} options");

			return ServiceResult.Success(Constants.Survey.DuplicateSuccess);
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService,
							$"Error duplicating survey ID: {surveyId}");
			return ServiceResult.Failure(Constants.Survey.DuplicateError);
		}
	}

	/// <summary>
	/// Submits survey responses and returns a completion code
	/// Note: Actual responses are handled by the delayed submission service for SHIELD compliance
	/// </summary>
	/// <param name="surveyId">ID of the survey</param>
	/// <param name="entryCode">Entry code used to access the survey</param>
	/// <param name="responses">List of responses to submit</param>
	/// <returns>A service result containing the completion code if successful</returns>
	public async Task<DataServiceResult<string>> SubmitSurveyAsync(Guid surveyId, string entryCode, List<Response> responses)
	{
		// NOTE: We intentionally don't log the user or specific response details here
		// due to SHIELD compliance requirements
		try
		{
			await using var dbContext = await contextFactory.CreateDbContextAsync();
			var survey = await dbContext.Surveys.FirstOrDefaultAsync(s => s.Id == surveyId);
			if (survey == null)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to submit to non-existent survey with ID: {surveyId}");
				return ServiceResult.Failure<string>(Constants.Survey.NotFound);
			}

			if (survey.State != SurveyStates.Running)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Attempt to submit to survey not in Running state. Survey ID: {surveyId}, State: {survey.State}");
				return ServiceResult.Failure<string>(Constants.Survey.NoSubmissions);
			}

			// Log that a submission attempt was made, but don't include the entry code or response details
			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Submission attempt for survey: {survey.Title} (ID: {surveyId}), Response count: {responses.Count}");

			var burnResult = await surveyCodeService.BurnEntryCodeAsync(entryCode);
			if (!burnResult)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning,
										  $"Failed to burn entry code for survey ID: {surveyId} - Code invalid or already used");
				return ServiceResult.Failure<string>(Constants.EntryCodes.InvalidOrUsed);
			}

			// Queue responses for delayed submission
			delayedSubmissionService.QueueResponses(responses);

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Successfully queued {responses.Count} responses for survey: {survey.Title} (ID: {surveyId})");

			// Get completion code
			var codeResult = await surveyCodeService.GetCompletionCodeAsync(surveyId);
			if (!codeResult.Successful)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Error,
										  $"Failed to generate completion code for survey ID: {surveyId}");
				return ServiceResult.Failure<string>(Constants.CompletionCodes.Error);
			}

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Successfully generated completion code for survey: {survey.Title} (ID: {surveyId})");
			return ServiceResult.Success(codeResult.Data, string.Empty);
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService, $"Error submitting survey ID: {surveyId}");
			return ServiceResult.Failure<string>($"Failed to submit survey: {ex.Message}");
		}
	}

	/// <summary>
	/// Creates an example survey with sample questions for demonstration purposes
	/// </summary>
	/// <param name="managerId">ID of the user who will be set as the survey manager</param>
	public async Task CreateExampleSurvey(Guid managerId)
	{
		var exampleSurvey = new Survey
		{
			Title = "The Survey after the Survey",
			ParticipationType = ParticipationTypes.Manual,
			ManagerId = managerId,
			CreationDate = DateTime.UtcNow,
			Description = "This is a quick test survey with different question types to show the entire flow in ImpowerSurvey.\n\nUse this to explore how surveys work and the different question types available.",
			State = SurveyStates.Created,
			Questions =
			[
				new Question
				{
					Text = "How satisfied are you with ImpowerSurvey?",
					Type = QuestionTypes.Rating,
					Options = []
				},
				new Question
				{
					Text = "What features are you most excited about?",
					Type = QuestionTypes.MultipleChoice,
					Options =
					[
						new QuestionOption { Text = "SHIELD Privacy" },
						new QuestionOption { Text = "Entry/Completion Codes" },
						new QuestionOption { Text = "Mobile Support" },
						new QuestionOption { Text = "Question Variety" },
						new QuestionOption { Text = "Analytics & Results" }
					]
				},
				new Question
				{
					Text = "What's your favorite part of the interface?",
					Type = QuestionTypes.SingleChoice,
					Options =
					[
						new QuestionOption { Text = "Clean Design" },
						new QuestionOption { Text = "Glass Cards" },
						new QuestionOption { Text = "Survey Creation" },
						new QuestionOption { Text = "Results View" }
					]
				},
				new Question
				{
					Text = "Do you have any additional thoughts about ImpowerSurvey?",
					Type = QuestionTypes.Text,
					Options = []
				}
			]
		};

		await using var dbContext = await contextFactory.CreateDbContextAsync();
		dbContext.Surveys.Add(exampleSurvey);
		await dbContext.SaveChangesAsync();

		await surveyCodeService.CreateDemoEntryCodeAsync(exampleSurvey.Id);
	}

	/// <summary>
	/// Creates a new survey
	/// </summary>
	/// <param name="survey">The survey to create</param>
	/// <returns>A service result containing the new survey ID if successful</returns>
	public async Task<DataServiceResult<Guid>> CreateSurveyAsync(Survey survey)
	{
		try
		{
			await using var dbContext = await contextFactory.CreateDbContextAsync();
			survey.CreationDate = DateTime.UtcNow;
			dbContext.Surveys.Add(survey);
			await dbContext.SaveChangesAsync();

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Created new survey: {survey.Title} (ID: {survey.Id}), Question count: {survey.Questions?.Count ?? 0}");
			return ServiceResult.Success(survey.Id, Constants.Survey.CreateSuccess);
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService, $"Error creating survey: {survey.Title}");
			return ServiceResult.Failure<Guid>(Constants.Survey.CreateError);
		}
	}

	/// <summary>
	/// Updates an existing survey
	/// </summary>
	/// <param name="survey">The updated survey data</param>
	/// <returns>A service result indicating success or failure</returns>
	public async Task<ServiceResult> UpdateSurveyAsync(Survey survey)
	{
		try
		{
			await using var dbContext = await contextFactory.CreateDbContextAsync();
			var existingSurvey = await dbContext.Surveys.AsSplitQuery()
												.Include(s => s.Questions)
												.ThenInclude(q => q.Options)
												.FirstOrDefaultAsync(s => s.Id == survey.Id);

			if (existingSurvey == null)
			{
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Warning, $"Attempt to update non-existent survey with ID: {survey.Id}");
				return ServiceResult.Failure(Constants.Survey.NotFound);
			}

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information, $"Updating survey: {existingSurvey.Title} (ID: {survey.Id})");
			var oldTitle = existingSurvey.Title;

			// Update basic survey properties
			existingSurvey.Title = survey.Title;
			existingSurvey.Description = survey.Description;

			if (oldTitle != survey.Title)
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Information, $"Survey title changed from '{oldTitle}' to '{survey.Title}'");

			// Handle questions
			var questionIdsToKeep = survey.Questions.Select(q => q.Id).Where(id => id != 0).ToList();

			// Remove questions that are no longer in the survey
			var questionsToRemove = existingSurvey.Questions.Where(q => !questionIdsToKeep.Contains(q.Id)).ToList();
			foreach (var question in questionsToRemove)
				existingSurvey.Questions.Remove(question);

			if (questionsToRemove.Count > 0)
				await logService.LogAsync(LogSource.SurveyService, LogLevel.Information, $"Removed {questionsToRemove.Count} questions from survey: {survey.Title} (ID: {survey.Id})");

			var addedQuestions = 0;
			var updatedQuestions = 0;
			var addedOptions = 0;
			var updatedOptions = 0;
			var removedOptions = 0;

			// Update existing questions and add new ones
			foreach (var updatedQuestion in survey.Questions)
				if (updatedQuestion.Id == 0)
				{
					// New question
					existingSurvey.Questions.Add(updatedQuestion);
					addedQuestions++;
				}
				else
				{
					// Update existing question
					var existingQuestion = existingSurvey.Questions.FirstOrDefault(q => q.Id == updatedQuestion.Id);
					if (existingQuestion == null)
						continue;

					existingQuestion.Text = updatedQuestion.Text;
					existingQuestion.Type = updatedQuestion.Type;
					updatedQuestions++;

					// Handle options based on question type
					if (updatedQuestion.Type != QuestionTypes.SingleChoice &&
						updatedQuestion.Type != QuestionTypes.MultipleChoice)
					{
						// Clear all options if the question type doesn't support options
						removedOptions += existingQuestion.Options.Count;
						existingQuestion.Options.Clear();
					}
					else
					{
						// Handle options for choice-type questions
						var optionIdsToKeep = updatedQuestion.Options.Select(o => o.Id).Where(id => id != 0).ToList();

						// Remove options that are no longer in the question
						var optionsToRemove = existingQuestion.Options.Where(o => !optionIdsToKeep.Contains(o.Id)).ToList();
						foreach (var option in optionsToRemove)
							existingQuestion.Options.Remove(option);

						removedOptions += optionsToRemove.Count;

						// Update existing options and add new ones
						foreach (var updatedOption in updatedQuestion.Options)
							if (updatedOption.Id == 0)
							{
								// New option
								existingQuestion.Options.Add(new QuestionOption { Text = updatedOption.Text });
								addedOptions++;
							}
							else
							{
								// Update existing option
								var existingOption = existingQuestion.Options.FirstOrDefault(o => o.Id == updatedOption.Id);
								if (existingOption != null)
								{
									existingOption.Text = updatedOption.Text;
									updatedOptions++;
								}
							}
					}
				}

			await dbContext.SaveChangesAsync();

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Successfully updated survey: {survey.Title} (ID: {survey.Id}). " +
									  $"Added: {addedQuestions} questions, {addedOptions} options. " +
									  $"Updated: {updatedQuestions} questions, {updatedOptions} options. " +
									  $"Removed: {questionsToRemove.Count} questions, {removedOptions} options.");

			return ServiceResult.Success(Constants.Survey.UpdateSuccess);
		}
		catch (Exception ex)
		{
			ex.LogException(logService, LogSource.SurveyService,
							$"Error updating survey ID: {survey.Id}");
			return ServiceResult.Failure(Constants.Survey.UpdateError);
		}
	}

	/// <summary>
	/// Gets a survey by its ID with optional includes for questions and options
	/// </summary>
	/// <param name="surveyId">The ID of the survey to retrieve</param>
	/// <param name="includeQuestions">Whether to include questions</param>
	/// <param name="includeOptions">Whether to include question options</param>
	/// <param name="asNoTracking">Whether to retrieve as no-tracking for read-only operations</param>
	/// <returns>The survey if found, otherwise null</returns>
	public async Task<Survey> GetSurveyByIdAsync(Guid surveyId, bool includeQuestions = false, bool includeOptions = false, bool asNoTracking = false)
	{
		await using var dbContext = await contextFactory.CreateDbContextAsync();

		var query = dbContext.Surveys.AsQueryable();

		if (asNoTracking)
			query = query.AsNoTracking();

		if (includeQuestions)
		{
			query = includeOptions
				? query.Include(s => s.Questions).ThenInclude(q => q.Options)
				: query.Include(s => s.Questions);
		}

		return await query.AsSplitQuery().FirstOrDefaultAsync(s => s.Id == surveyId);
	}

	/// <summary>
	/// Gets all surveys with optional filtering by state
	/// </summary>
	/// <param name="includeManager">Whether to include the manager user object</param>
	/// <param name="asNoTracking">Whether to retrieve as no-tracking for read-only operations</param>
	/// <param name="filterState">Optional state to filter by</param>
	/// <param name="includeQuestions">Whether to include the Questions</param>
	/// <param name="includeResponses">Whether to include the Responses</param>
	/// <returns>List of surveys matching the criteria</returns>
	public async Task<List<Survey>> GetAllSurveysAsync(bool includeManager = false, bool includeQuestions = false, bool includeResponses = false, bool asNoTracking = false, SurveyStates? filterState = null)
	{
		await using var dbContext = await contextFactory.CreateDbContextAsync();

		var query = dbContext.Surveys.AsQueryable();

		if (asNoTracking)
			query = query.AsNoTracking();

		if (includeManager)
			query = query.Include(x => x.Manager);

		if (includeQuestions)
			query = query.Include(x => x.Questions);

		if (includeResponses)
			query = query.Include(x => x.Responses);

		if (filterState.HasValue)
			query = query.Where(x => x.State == filterState.Value);

		return await query.AsSplitQuery().ToListAsync();
	}

	/// <summary>
	/// Gets participation statistics for a survey
	/// </summary>
	/// <param name="surveyId">The ID of the survey</param>
	/// <returns>A service result containing the participation statistics</returns>
	public async Task<DataServiceResult<SurveyParticipationStats>> GetSurveyParticipationStatsAsync(Guid surveyId)
	{
		await using var dbContext = await contextFactory.CreateDbContextAsync();

		async Task<List<ParticipationRecord>> GetParticipationRecordsAsync(SurveyDbContext dbContext)
		{
			var usedCompletionCodeIds = await dbContext.CompletionCodes
													   .Where(x => x.SurveyId == surveyId && x.IsUsed)
													   .Select(x => x.Id)
													   .ToListAsync();

			return await dbContext.ParticipationRecords
								  .Where(x => usedCompletionCodeIds.Contains(x.CompletionCodeId))
								  .ToListAsync();
		}

		var survey = await dbContext.Surveys.AsNoTracking().Where(s => s.Id == surveyId).FirstOrDefaultAsync();

		List<ParticipationRecord> participationRecords;
		if (survey?.State == SurveyStates.Closed && (survey.IssuedEntryCodesCount > 0 || survey.UsedEntryCodesCount > 0 || survey.SubmittedCompletionCodesCount > 0))
		{
			participationRecords = await GetParticipationRecordsAsync(dbContext);
			// For closed surveys with stored stats, return those instead of querying tables
			return ServiceResult.Success(new SurveyParticipationStats
			{
				IssuedEntryCodes = survey.IssuedEntryCodesCount,
				UsedEntryCodes = survey.UsedEntryCodesCount,
				SubmittedCompletionCodes = survey.SubmittedCompletionCodesCount,
				ParticipationRecords = participationRecords
			}, "");
		}

		// For surveys that are not closed or don't have stats stored yet, query directly
		var issuedEntryCodes = await dbContext.EntryCodes.CountAsync(x => x.SurveyId == surveyId && x.IsIssued);
		var usedEntryCodes = await dbContext.EntryCodes.CountAsync(x => x.SurveyId == surveyId && x.IsIssued && x.IsUsed);
		var submittedCompletionCodes = await dbContext.CompletionCodes.CountAsync(x => x.SurveyId == surveyId && x.IsUsed);

		participationRecords = await GetParticipationRecordsAsync(dbContext);

		return ServiceResult.Success(new SurveyParticipationStats
		{
			IssuedEntryCodes = issuedEntryCodes,
			UsedEntryCodes = usedEntryCodes,
			SubmittedCompletionCodes = submittedCompletionCodes,
			ParticipationRecords = participationRecords
		}, string.Empty);
	}

	/// <summary>
	/// Gets a survey with its questions, options, and responses for result analysis
	/// </summary>
	/// <param name="surveyId">The ID of the survey</param>
	/// <returns>The survey with its results if found, otherwise null</returns>
	public async Task<Survey> GetSurveyResults(Guid surveyId)
	{
		await using var dbContext = await contextFactory.CreateDbContextAsync();
		var survey = await dbContext.Surveys.AsNoTracking().AsSplitQuery()
									.Include(s => s.Questions)
									.ThenInclude(q => q.Options)
									.Include(x => x.Responses)
									.FirstOrDefaultAsync(s => s.Id == surveyId);

		return survey;
	}
}
