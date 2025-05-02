using ImpowerSurvey.Components.Model;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ImpowerSurvey.Services;

/// <summary>
/// Extension methods for HttpClient for inter-instance communication
/// </summary>
public static class HttpClientExtensions
{
	/// <summary>
	/// The named HttpClient used for inter-instance communication
	/// </summary>
	public const string InstanceHttpClientName = "InstanceClient";
	
	/// <summary>
	/// Perform a NoOp communication test to verify inter-instance communication is working
	/// </summary>
	public static async Task<DataServiceResult<bool>> VerifyInstanceCommunicationAsync(this IHttpClientFactory clientFactory, string leaderUrl, string instanceId, string instanceSecret)
	{
		var payload = new InstanceCommunicationPayload 
		{ 
			SourceInstanceId = instanceId,
			CommunicationType = InstanceCommunicationType.NoOp
		};
		
		return await SendRequestToLeaderAsync(clientFactory, leaderUrl, instanceSecret, payload, true, "Inter-instance communication verified successfully");
	}
	
	/// <summary>
	/// Common method to send requests to the leader instance and process responses
	/// </summary>
	private static async Task<DataServiceResult<T>> SendRequestToLeaderAsync<T>(IHttpClientFactory clientFactory, string leaderUrl, string instanceSecret, 
																						InstanceCommunicationPayload payload, T defaultSuccessValue, string successMessage)
	{
		try
		{
			// Prepare the URL
			var url = $"{leaderUrl.TrimEnd('/')}/api/internal/responses/transfer";
			
			// Get the named instance client from factory
			using var instanceClient = clientFactory.CreateClient(InstanceHttpClientName);
			
			// Configure the client with the instance secret - needed for each client instance when using factory
			if (!string.IsNullOrEmpty(instanceSecret))
			{
				// Remove any existing auth header first to avoid duplicates
				if (instanceClient.DefaultRequestHeaders.Contains(Constants.App.AuthHeaderName))
					instanceClient.DefaultRequestHeaders.Remove(Constants.App.AuthHeaderName);
					
				// Add the auth header
				instanceClient.DefaultRequestHeaders.Add(Constants.App.AuthHeaderName, instanceSecret);
			}
			
			// Send the request
			var response = await instanceClient.PostAsJsonAsync(url, payload);
			
			// Process the response
			if (response.IsSuccessStatusCode)
			{
				var result = await response.Content.ReadFromJsonAsync<DataServiceResult<T>>();
				return result ?? ServiceResult.Success(defaultSuccessValue, successMessage);
			}

			// Handle error response
			var errorContent = await response.Content.ReadAsStringAsync();
			return ServiceResult.Failure<T>($"Request failed with status code: {response.StatusCode}. Details: {errorContent}");
		}
		catch (Exception ex)
		{
			return ServiceResult.Failure<T>($"Exception during request: {ex.Message}");
		}
	}

	/// <summary>
	/// Transfers responses to the leader instance
	/// </summary>
	public static async Task<DataServiceResult<int>> TransferResponsesToLeaderAsync(this IHttpClientFactory clientFactory, string leaderUrl, string instanceId, 
																					List<Response> responses, string instanceSecret)
	{
		var payload = new InstanceCommunicationPayload 
		{ 
			SourceInstanceId = instanceId,
			CommunicationType = InstanceCommunicationType.TransferResponses,
			Responses = responses
		};
		
		return await SendRequestToLeaderAsync(clientFactory, leaderUrl, instanceSecret, payload, responses.Count, "Responses transferred successfully");
	}

	/// <summary>
	/// Requests the leader instance to close a survey
	/// </summary>
	public static async Task<DataServiceResult<bool>> CloseSurveyAsync(this IHttpClientFactory clientFactory, string leaderUrl, string instanceId, Guid surveyId, string instanceSecret)
	{
		var payload = new InstanceCommunicationPayload 
		{ 
			SourceInstanceId = instanceId,
			CommunicationType = InstanceCommunicationType.CloseSurvey,
			SurveyId = surveyId
		};
		
		return await SendRequestToLeaderAsync(clientFactory, leaderUrl, instanceSecret, payload, true, "Survey close request successful");
	}
}