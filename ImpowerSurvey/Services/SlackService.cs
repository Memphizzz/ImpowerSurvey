using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.WebApi;
using User = ImpowerSurvey.Components.Model.User;

namespace ImpowerSurvey.Services;

/// <summary>
/// Service that implements the Slack integration for sending surveys, notifications,
/// and processing survey completion codes
/// </summary>
public partial class SlackService(IWebHostEnvironment environment, ISlackApiClient slackClient, 
	IConfiguration config, UserService userService, ILogService logService) : IHostedService, ISlackService
{
	// WARNING: DO NOT CONVERT TO PRIMARY CONSTRUCTOR PARAMETERS!
	// This is a technical limitation with C# primary constructors when implementing interfaces:
	// 1. Primary constructor parameters are only correctly accessible from methods declared in the interface
	//    (e.g., StartAsync and StopAsync from IHostedService)
	// 2. When called from non-interface methods, the compiler-generated backing fields will be null at runtime
	// 3. This causes NullReferenceExceptions despite the code appearing syntactically correct
	//
	// ReSharper incorrectly suggests converting these fields to primary constructor parameters, but doing so
	// would break functionality in all methods that aren't part of IHostedService.

	// ReSharper disable ReplaceWithPrimaryConstructorParameter
	private readonly ISlackApiClient _slackClient = slackClient;
	private readonly IWebHostEnvironment _environment = environment;
	private readonly UserService _userService = userService;
	private readonly ILogService _logService = logService;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter
	private static string _serverUrl;
	private static AuthTestResponse _auth;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			_auth = await _slackClient.Auth.Test(cancellationToken);

			_serverUrl = Environment.GetEnvironmentVariable(Constants.App.EnvHostUrl, EnvironmentVariableTarget.Process) ??
						 config.GetValue<string>(Constants.App.EnvHostUrl) ??
						 config["URLS"]?.Split(';')?.FirstOrDefault(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
			
			if (string.IsNullOrWhiteSpace(_serverUrl))
			{
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Critical, Constants.InvalidHostURL);
				throw new Exception(Constants.InvalidHostURL);
			}

			if (_serverUrl.EndsWith('/'))
				_serverUrl = _serverUrl[..^1];

			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
									   $"SlackService started. Server URL: {_serverUrl}, Environment: {_environment.EnvironmentName}, " +
									   $"Bot Name: {_auth.User}, Team: {_auth.Team}");

			if (_environment.IsProduction())
			{
				await Notify($"SlackService for InstanceID '{LeaderElectionService.InstanceId}' serving {_serverUrl}..", Roles.Admin);
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
					"Startup notification sent to admins");
			}
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SlackService, "Failed to start SlackService");
			throw; // Rethrow to prevent the service from starting in an invalid state
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"SlackService for InstanceID '{LeaderElectionService.InstanceId}' shutting down");
				
			if (_environment.IsProduction())
			{
				await Notify($"SlackService for InstanceID '{LeaderElectionService.InstanceId}' shutting down", Roles.Admin);
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
					"Shutdown notification sent to admins");
			}
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SlackService, "Error during SlackService shutdown");
		}
	}

	public async Task<List<string>> VerifyParticipants(List<string> participants)
	{
		var invalidEmails = new List<string>();

		foreach (var email in participants)
			try
			{
				await _slackClient.Users.LookupByEmail(email);
			}
			catch (SlackException e)
			{
				if (e.Message.Contains(Constants.Slack.API.UsersNotFound))
					invalidEmails.Add(email);
				else
					await _logService.LogAsync(LogSource.SlackService, LogLevel.Error, 
						$"Slack API Error ({e.ErrorCode}): {string.Join(", ", e.ErrorMessages)}");
			}
			catch (Exception ex)
			{
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Error, 
					$"Error verifying Slack participants: {ex.Message}");
			}

		return invalidEmails;
	}

	public async Task<bool> SendSurveyInvitation(string email, Guid surveyId, string surveyTitle, User manager, string entryCode)
	{
		try
		{
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"Sending survey invitation for survey ID: {surveyId}, Title: '{surveyTitle}' to: {email}");
				
			var user = await _slackClient.Users.LookupByEmail(email);
			await _slackClient.Chat.PostMessage(new Message
			{
				Channel = user.Id,
				Blocks =
				[
					new HeaderBlock { Text = new PlainText(Constants.Slack.SurveyHeader) },
					new SectionBlock { Text = new Markdown(string.Format(Constants.Slack.SurveyGreeting, Utilities.GetTimezoneAwareGreeting(manager.TimeZone, _logService), user.RealName)) },
					new SectionBlock { Text = new Markdown(string.Format(Constants.Slack.SurveyParticipationRequest, manager.DisplayName)) },
					new SectionBlock { Text = new Markdown(string.Format(Constants.Slack.SurveyTitle, surveyTitle)) },
					new SectionBlock { Text = new Markdown(Constants.Slack.SurveyYourEntryCode) },
					new SectionBlock { Text = new Markdown(string.Format(Constants.Slack.SurveyEntryCode, entryCode)) },
					new SectionBlock { Text = new Markdown(string.Format(Constants.Slack.SurveyLink, _serverUrl)) },
					new SectionBlock { Text = new Markdown(string.Format(Constants.Slack.SurveyLinkMobile, $"{_serverUrl}/mobile")) },
					new DividerBlock(),
					new InputBlock
					{
						BlockId = string.Format(Constants.Slack.API.CompletionCodeInputIdentifier, surveyId),
						Label = Constants.Slack.SurveyOnceCompletedLabel,
						Element = new PlainTextInput
						{
							ActionId = string.Format(Constants.Slack.API.CompletionCodeIdentifier, surveyId),
							Placeholder = new PlainText(Constants.Slack.SurveyEnterCompletionCode)
						}
					},
					new ActionsBlock
					{
						Elements =
						[
							new Button
							{
								Text = new PlainText(Constants.Slack.SurveySubmitCompletionCode),
								ActionId = Constants.Slack.API.CompletionCodeSubmitIdentifier,
								Style = ButtonStyle.Primary
							}
						]
					}
				]
			});
			
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"Successfully sent survey invitation to {email} for survey ID: {surveyId}");
				
			return true;
		}
		catch (SlackException e)
		{
			var errorMessage = e.Message.Contains(Constants.Slack.API.UsersNotFound)
				? $"Failed to send invitation - user not found in Slack: {email}"
				: $"Failed to send invitation to {email}: {e.Message}";
				
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Error, errorMessage);
			return false;
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SlackService, 
				$"Exception sending invitation to {email} for survey ID: {surveyId}");
			return false;
		}
	}

	public async Task<int> SendBulkMessages(List<string> emails, string message, string context = "bulk message", string timeZone = null)
	{
		try
		{
			if (emails == null || !emails.Any())
				return 0;
				
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"Sending {context} to {emails.Count} recipients");
				
			// Verify if the participants exist in Slack
			var invalidEmails = await VerifyParticipants(emails);
			var validEmails = emails.Except(invalidEmails).ToList();
			
			if (validEmails.Count == 0)
			{
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Warning, 
					$"No valid recipients found for {context}");
				return 0;
			}
			
			var successCount = 0;
			
			// Send individual messages to each recipient
			var errors = new List<string>();
			foreach (var email in validEmails)
			{
					var result = await SendPersonalizedMessage(email, message, timeZone);
					if (result)
						successCount++;
					else
						errors.Add(email);
			}
			
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"Successfully sent {successCount}/{validEmails.Count} {context}. Invalid emails: {invalidEmails.Count}, Valid emails: {validEmails.Count}, Errors: {validEmails.Count - successCount} {string.Join(", ", errors)}");
				
			return successCount;
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SlackService, 
				$"Error sending {context}: {ex.Message}");
			return 0;
		}
	}

	/// <summary>
	/// Sends a personalized message to a Slack user with an appropriate greeting
	/// </summary>
	/// <param name="email">The email address of the Slack user</param>
	/// <param name="message">The message content to send</param>
	/// <param name="timeZone">Optional timezone for time-aware greeting</param>
	/// <returns>True if the message was sent successfully, otherwise false</returns>
	private async Task<bool> SendPersonalizedMessage(string email, string message, string timeZone = null)
	{
		try
		{
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
									   $"Sending personalized message to: {email}", true);
				
			var user = await _slackClient.Users.LookupByEmail(email);
			var greeting = string.Format(Constants.Slack.SurveyGreeting, Utilities.GetTimezoneAwareGreeting(timeZone, _logService), user.RealName);
			await _slackClient.Chat.PostMessage(new Message
			{
				Channel = user.Id,
				Text = $"{greeting} {message}"
			});
			
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, $"Successfully sent personalized message to {email}", true);
			return true;
		}
		catch (SlackException e)
		{
			var errorMessage = e.Message.Contains(Constants.Slack.API.UsersNotFound)
				? $"Failed to send message - user not found in Slack: {email}"
				: $"Failed to send message to {email}: {e.Message}";
				
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Error, errorMessage, true);
			return false;
		}
		catch (Exception ex)
		{
			ex.LogException(_logService, LogSource.SlackService, 
							$"Exception sending message to {email}");
			return false;
		}
	}

	public async Task Notify(string message, params Roles[] roles)
	{
		if (roles.Length == 0 || roles.Length == 1 && roles[0] == Roles.SurveyParticipant)
			return;

		await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
			$"Sending notification to roles: {string.Join(", ", roles)}, Message: '{message}'");
			
		var users = await _userService.GetUsersByRoles(roles);
		var failures = new List<string>();
		var successCount = 0;

		void AddFailure(User user, string reason = null)
		{
			var failureInfo = $"{user.Username} ({user.Emails.Values.FirstOrDefault() ?? "no email"})";
			if (!string.IsNullOrEmpty(reason))
				failureInfo += $" - {reason}";
				
			failures.Add(failureInfo);
		}

		foreach (var user in users)
		{
			if (!user.Emails.TryGetValue(ParticipationTypes.Slack, out var slackEmail) || string.IsNullOrEmpty(slackEmail))
			{
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Warning, 
					$"Cannot notify user {user.Username} - No Slack email configured");
				AddFailure(user, "No Slack email");
				continue;
			}

			try
			{
				var slackUser = await _slackClient.Users.LookupByEmail(slackEmail);
				await _slackClient.Chat.PostMessage(new Message
				{
					Channel = slackUser.Id,
					Text = message
				});
				
				successCount++;
			}
			catch (SlackException e)
			{
				if (e.Message.Contains(Constants.Slack.API.UsersNotFound))
				{
					AddFailure(user, "Not found in Slack");
					continue;
				}

				var errorMessage = $"Slack API Error for user {user.Username}: {string.Join(", ", e.ErrorMessages)}";
				
				
				AddFailure(user, $"Slack API Error: {e.ErrorCode}");
			}
			catch (Exception ex)
			{
				var errorMessage = $"{nameof(SlackService)}.{nameof(Notify)}: {ex.Message}";
				ex.LogException(_logService, LogSource.SlackService, 
					$"Error notifying user {user.Username} with email {slackEmail}");
				AddFailure(user, "Exception");
			}
		}

		if (failures.Count > 0)
		{
			var errorMessage = $"Failed to notify {failures.Count} {string.Join(" & ", roles.Select(x => $"{x}s"))}: {string.Join(", ", failures)}";
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Warning, errorMessage);
		}
		
		if (successCount > 0)
		{
			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"Successfully notified {successCount} users with message: '{message}'");
		}
	}
}