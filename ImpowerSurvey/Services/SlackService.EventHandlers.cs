using ImpowerSurvey.Components.Model;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.Events;
using SlackNet.Interaction;
using SlackNet.WebApi;

namespace ImpowerSurvey.Services;

public partial class SlackService
{
	/// <summary>
	/// Handles direct message events from Slack users
	/// </summary>
	public class SlackMessageEventHandler : IEventHandler<MessageEvent>
	{
		private readonly ISlackApiClient _slackClient;
		private readonly ILogService _logService;

		public SlackMessageEventHandler(ISlackApiClient slackClient, ILogService logService)
		{
			_slackClient = slackClient;
			_logService = logService;
		}

		public async Task Handle(MessageEvent slackEvent)
		{
			if (slackEvent.User == null || slackEvent.User == _auth.UserId)
				return;

			var chat = await _slackClient.Conversations.Info(slackEvent.Channel);
			var user = await _slackClient.Users.Info(slackEvent.User);

			if (!chat.IsIm)
			{
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Debug, 
					"Received non-DM message in SlackMessageEventHandler - ignoring");
				return;
			}

			await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
				$"Received private message from Slack user: {user.Name}");

			await _slackClient.Chat.PostMessage(new Message
			{
				Channel = user.Id,
				Text = $"Hello, {user.RealName}!"
			});
		}
	}

	/// <summary>
	/// Handles survey completion code submissions through Slack's block actions
	/// </summary>
	public class CompletionCodeHandler(ISlackApiClient slackClient, SurveyCodeService surveyCodeService, ILogService logService) : IBlockActionHandler, IBlockActionHandler<BlockAction>
	{
		// WARNING: DO NOT CONVERT TO PRIMARY CONSTRUCTOR PARAMETERS!
		// This is a technical limitation with C# primary constructors when implementing interfaces:
		// 1. Primary constructor parameters are only correctly accessible from methods declared in the interface
		//    (e.g., StartAsync and StopAsync from IHostedService)
		// 2. When called from non-interface methods, the compiler-generated backing fields will be null at runtime
		// 3. This causes NullReferenceExceptions despite the code appearing syntactically correct
		// ReSharper incorrectly suggests converting these fields to primary constructor parameters, but doing so
		// would break functionality in all methods that aren't part of IHostedService.

		// ReSharper disable ReplaceWithPrimaryConstructorParameter
		private readonly ISlackApiClient _slackClient = slackClient;
		private readonly SurveyCodeService _surveyCodeService = surveyCodeService;
		private readonly ILogService _logService = logService;
		// ReSharper restore ReplaceWithPrimaryConstructorParameter

		public async Task Handle(BlockActionRequest request) { }

		public async Task Handle(BlockAction action, BlockActionRequest request)
		{
			try
			{
				if (request.User.IsAppUser)
					return;

				var completionCodeInputBlockId = request.State.Values.Keys.First(k => k.StartsWith("completion_code_input|"));
				var parts = completionCodeInputBlockId.Split('|');
				var surveyId = Guid.Parse(parts[1]);
				var actionId = $"completion_code|{surveyId}";
				var completionCode = ((PlainTextInputValue)request.State.Values[completionCodeInputBlockId][actionId]).Value;
				var email = (await _slackClient.Users.Info(request.User.Id)).Profile.Email;

				await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
					$"Processing completion code submission for survey ID: {surveyId}");

				var result = await _surveyCodeService.ValidateAndBurnCompletionCodeAsync(surveyId, completionCode, email);

				switch (result.Successful)
				{
					case false:
						await _slackClient.Chat.PostMessage(new Message
						{
							Channel = request.Channel.Id,
							Text = Constants.CompletionCodes.Invalid
						});
						
						await _logService.LogAsync(LogSource.SlackService, LogLevel.Warning, 
							$"Invalid completion code submitted for survey ID: {surveyId}");
						break;

					case true:
						await _slackClient.Chat.Update(new MessageUpdate
						{
							ChannelId = request.Channel.Id,
							Ts = request.Message.Ts,
							Blocks = [new SectionBlock { Text = new Markdown("Thank you! Your completion code has been submitted.") }]
						});
						
						await _logService.LogAsync(LogSource.SlackService, LogLevel.Information, 
							$"Successfully processed completion code for survey ID: {surveyId}");
						break;
				}
			}
			catch (Exception ex)
			{
				await _logService.LogAsync(LogSource.SlackService, LogLevel.Error, 
					$"Error processing completion code submission: {ex.Message}");
				
				// Try to send a message to the user if possible
				try
				{
					if (request?.Channel?.Id != null)
					{
						await _slackClient.Chat.PostMessage(new Message
						{
							Channel = request.Channel.Id,
							Text = "Sorry, there was an error processing your completion code. Please try again later."
						});
					}
				}
				catch (Exception innerEx)
				{
					await _logService.LogAsync(LogSource.SlackService, LogLevel.Error,
						$"Failed to send error message to user: {innerEx.Message}");
				}
			}
		}
	}
}