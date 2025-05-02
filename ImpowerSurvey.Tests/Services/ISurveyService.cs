using ImpowerSurvey.Components.Model;

namespace ImpowerSurvey.Tests.Services
{
    /// <summary>
    /// Interface for SurveyService methods needed for testing
    /// </summary>
    public interface ISurveyService 
    {
        Task<ServiceResult> SendInvites(Guid surveyId, List<string> participants);
        Task<ServiceResult> CloseSurvey(Guid surveyId);
    }
}