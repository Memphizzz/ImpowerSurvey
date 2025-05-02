using ImpowerSurvey.Components.Model;

namespace ImpowerSurvey.Services
{
    /// <summary>
    /// Interface for the SlackService that defines all public operations
    /// </summary>
    public interface ISlackService
    {
        /// <summary>
        /// Verifies if the provided participants exist in Slack
        /// </summary>
        /// <param name="participants">List of email addresses to verify</param>
        /// <returns>List of email addresses that were not found in Slack</returns>
        Task<List<string>> VerifyParticipants(List<string> participants);
        
        /// <summary>
        /// Sends a survey invitation to a Slack user
        /// </summary>
        /// <param name="email">Email address of recipient</param>
        /// <param name="surveyId">ID of the survey</param>
        /// <param name="surveyTitle">Title of the survey</param>
        /// <param name="manager">Manager user who created the survey</param>
        /// <param name="entryCode">Entry code for the survey</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> SendSurveyInvitation(string email, Guid surveyId, string surveyTitle, User manager, string entryCode);

		/// <summary>
		/// Sends messages in bulk to multiple Slack users
		/// </summary>
		/// <param name="emails">List of email addresses to send to</param>
		/// <param name="message">Message content</param>
		/// <param name="context">Context description for logging</param>
		/// <param name="timeZone">The timezone of the recipients for a time-based greeting</param>
		/// <returns>Number of successful deliveries</returns>
		Task<int> SendBulkMessages(List<string> emails, string message, string context = "bulk message", string timeZone = null);
        
        /// <summary>
        /// Sends a notification to users with specific roles
        /// </summary>
        /// <param name="message">Message content</param>
        /// <param name="roles">Roles to notify</param>
        Task Notify(string message, params Roles[] roles);
    }
}