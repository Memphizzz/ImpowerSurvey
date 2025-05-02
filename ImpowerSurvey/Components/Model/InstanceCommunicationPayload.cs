namespace ImpowerSurvey.Components.Model;

/// <summary>
/// Payload for inter-instance communication operations
/// </summary>
public class InstanceCommunicationPayload
{
    /// <summary>
    /// The instance ID of the source instance
    /// </summary>
    public string SourceInstanceId { get; set; }
    
    /// <summary>
    /// The type of communication being performed
    /// </summary>
    public InstanceCommunicationType CommunicationType { get; set; } = InstanceCommunicationType.TransferResponses;
    
    /// <summary>
    /// The responses to transfer (used for TransferResponses operation)
    /// </summary>
    public List<Response> Responses { get; set; } = new List<Response>();
    
    /// <summary>
    /// The ID of the survey to flush (used for FlushSurvey operation)
    /// </summary>
    public Guid? SurveyId { get; set; }
}