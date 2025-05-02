using Anthropic;
using ImpowerSurvey.Components.Model;

namespace ImpowerSurvey.Services
{
	public interface IClaudeService
	{
		Task<string> AnonymizeTextAsync(string text);
		Task<List<string>> BatchAnonymizeAsync(List<string> texts);
		Task<string> GenerateSurveySummaryAsync(Survey survey, List<Response> responses);
		Task<List<string>> GetAvailableModelsAsync();
	}

	public class ClaudeService(ILogService logger, ISettingsService settingsService, ClaudeOptions options) : IClaudeService, IHostedService
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
		private readonly ILogService _logger = logger;
		private readonly ISettingsService _settingsService = settingsService;
		private readonly ClaudeOptions _options = options;
		// ReSharper restore ReplaceWithPrimaryConstructorParameter

		private AnthropicClient _client;
		private bool _isInitialized;

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			try
			{
				if (string.IsNullOrEmpty(_options.ApiKey))
				{
					await _logger.LogAsync(LogSource.ClaudeService, LogLevel.Warning, "Claude API key not found. Claude service will not be available.");
					_isInitialized = false;
					return;
				}

				_client = new AnthropicClient(_options.ApiKey);
				_isInitialized = true;
				await _logger.LogAsync(LogSource.ClaudeService, LogLevel.Information, "Claude service initialized successfully");
			}
			catch (Exception ex)
			{
				_isInitialized = false;
				await _logger.LogExceptionAsync(ex, LogSource.ClaudeService, "Failed to initialize Claude Service");
			}
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_client?.Dispose();
			_isInitialized = false;
		}

		private async Task<string> GetModelNameAsync()
		{
			// If model name is provided in options, use it
			if (!string.IsNullOrEmpty(_options.ModelName))
				return _options.ModelName;

			// Otherwise get it from settings
			return await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.ClaudeModel);
		}

		private async Task<bool> IsEnabledAsync()
		{
			return await _settingsService.GetBoolSettingAsync(Constants.SettingsKeys.ClaudeEnabled, true);
		}

		public async Task<string> AnonymizeTextAsync(string text)
		{
			if (string.IsNullOrEmpty(text))
				return text;

			if (!_isInitialized || !await IsEnabledAsync())
			{
				await _logger.LogAsync(LogSource.ClaudeService, LogLevel.Information, 
					!_isInitialized 
						? "Claude service is not properly initialized, returning original text" 
						: "Claude AI features are disabled, returning original text");
				return text;
			}

			try
			{
				var modelName = await GetModelNameAsync();

				// Get custom anonymization prompt from settings
				var promptTemplate = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.ClaudeAnonymizePrompt);

				// Combine security guards with system prompt-like formatting for maximum safety
				var prompt = string.Format(Constants.AI.Prompt, promptTemplate, text);

				var messageParams = new CreateMessageParams
				{
					Model = new Model(modelName),
					Messages = [ new InputMessage(InputMessageRole.User, prompt) ],
					MaxTokens = 1000
				};

				var response = await _client.Messages.MessagesPostAsync(messageParams);
				var content = response.Content.Where(x => x.IsText).Select(x => x.Text?.Text).ToList();
				return string.Join(". ", content);
			}
			catch (Exception ex)
			{
				await _logger.LogExceptionAsync(ex, LogSource.ClaudeService, "Anonymizing text");
				return text; // Return original text on error
			}
		}

		public async Task<List<string>> BatchAnonymizeAsync(List<string> texts)
		{
			if (texts == null || texts.Count == 0)
				return [];

			if (!await IsEnabledAsync() || !_isInitialized)
			{
				await _logger.LogAsync(LogSource.ClaudeService, LogLevel.Information, 
					!_isInitialized 
						? "Claude service is not properly initialized, returning original texts" 
						: "Claude AI features are disabled, returning original texts");
				return texts.ToList();
			}

			var results = new List<string>();
			foreach (var text in texts)
			{
				var anonymized = await AnonymizeTextAsync(text);
				results.Add(anonymized);
			}

			return results;
		}

		public async Task<string> GenerateSurveySummaryAsync(Survey survey, List<Response> responses)
		{
			if (survey == null || responses == null || responses.Count == 0)
				return "Insufficient data to generate summary.";

			if (!await IsEnabledAsync() || !_isInitialized)
			{
				await _logger.LogAsync(LogSource.ClaudeService, LogLevel.Information, 
					!_isInitialized 
						? "Claude service is not properly initialized, returning basic summary" 
						: "Claude AI features are disabled, returning basic summary");
				return !_isInitialized 
					? "AI summary generation is unavailable. Claude service is not properly initialized." 
					: "AI summary generation is disabled. Enable Claude AI features in settings to generate detailed summaries.";
			}

			try
			{
				var modelName = await GetModelNameAsync();
				var basePrompt = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.ClaudeSummaryPrompt);
				var prompt = BuildSurveySummaryPrompt(survey, responses, basePrompt);

				var messageParams = new CreateMessageParams
				{
					Model = new Model(modelName), 
					Messages = [new InputMessage(InputMessageRole.User, prompt)], 
					MaxTokens = 2000
				};

				var response = await _client.Messages.MessagesPostAsync(messageParams);
				var content = response.Content.Where(x => x.IsText).Select(x => x.Text?.Text).ToList();
				return string.Join("\n", content);
			}
			catch (Exception ex)
			{
				await _logger.LogExceptionAsync(ex, LogSource.ClaudeService, "Generating survey summary");
				return "Unable to generate survey summary due to an error.";
			}
		}

		/// <summary>
		/// Gets a list of available Claude models from the API
		/// </summary>
		public async Task<List<string>> GetAvailableModelsAsync()
		{
			if (!_isInitialized)
			{
				await _logger.LogAsync(LogSource.ClaudeService, LogLevel.Warning, "Cannot get available models, Claude service is not initialized");
				return [];
			}
			
			try
			{
				var models = await _client.ModelsListAsync();
				return models.Data.Select(m => m.Id).OrderBy(id => id).ToList();
			}
			catch (Exception ex)
			{
				await _logger.LogExceptionAsync(ex, LogSource.ClaudeService, "Getting available models");
				return [];
			}
		}

		/// <summary>
		/// Builds the prompt used to instruct Claude on how to summarize the survey
		/// </summary>
		/// <param name="survey">The survey to summarize</param>
		/// <param name="responses">The responses of the survey participants</param>
		/// <param name="basePrompt">The base prompt (instructions) to use</param>
		/// <returns>A detailed prompt for Claude on how to summarize the survey</returns>
		private static string BuildSurveySummaryPrompt(Survey survey, List<Response> responses, string basePrompt)
		{
			var prompt = $"{basePrompt}\n\n";
			prompt += $"Survey Title: {survey.Title}\n";
			prompt += $"Survey Description: {survey.Description}\n\n";

			// Group responses by question
			var questionResponses = responses.GroupBy(r => r.QuestionId);

			prompt += "Survey Questions and Responses:\n";
			foreach (var group in questionResponses)
			{
				var question = survey.Questions.FirstOrDefault(q => q.Id == group.Key);
				if (question == null)
					continue;

				prompt += $"Question: {question.Text} (Type: {question.Type})\n";
				prompt += "Responses:\n";

				prompt = group.Aggregate(prompt, (current, response) => current + $"- {response.Answer}\n");
				prompt += "\n";
			}

			prompt += "Please provide your analysis as HTML content that can be directly embedded in a report. Format your response with the following sections:\n\n";
			prompt += "<h3>Key Trends and Patterns</h3>\n";
			prompt += "<h3>Participant Sentiment</h3>\n";
			prompt += "<h3>Main Themes</h3>\n";
			prompt += "<h3>Notable Outliers</h3>\n";
			prompt += "<h3>Actionable Insights</h3>\n\n";
			prompt += "Use HTML tags like <h3>, <p>, <ul>, <li>, <strong>, and <em> for formatting. Do NOT use markdown formatting.";

			return prompt;
		}
	}
}