using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ImpowerSurvey.Components.Model
{
	/// <summary>
	/// Represents a log entry in the system
	/// </summary>
	public class Log
	{
		/// <summary>
		/// The unique identifier for this log entry
		/// </summary>
		public int Id { get; set; }
		
		/// <summary>
		/// The timestamp when this log was created
		/// </summary>
		public DateTime Timestamp { get; set; } = DateTime.UtcNow;
		
		/// <summary>
		/// The level of the log (Information, Warning, Error, etc.)
		/// </summary>
		[MaxLength(20)]
		public string Level { get; set; }
		
		/// <summary>
		/// The source component or service that generated the log
		/// </summary>
		[MaxLength(100)]
		public string Source { get; set; }
		
		/// <summary>
		/// The message for this log entry
		/// </summary>
		public string Message { get; set; }
		
		/// <summary>
		/// The user associated with this log entry, if applicable (may be null)
		/// </summary>
		[MaxLength(100)]
		public string User { get; set; }
		
		/// <summary>
		/// If true, this log entry contains data related to user identities.
		/// Used to enforce SHIELD compliance.
		/// </summary>
		public bool ContainsIdentityData { get; set; }
		
		/// <summary>
		/// If true, this log entry contains data related to survey responses.
		/// Used to enforce SHIELD compliance.
		/// </summary>
		public bool ContainsResponseData { get; set; }
		
		/// <summary>
		/// Additional structured data for this log entry, stored as JSON
		/// </summary>
		public string Data { get; set; }
		
		/// <summary>
		/// Serialize an object into the Data property
		/// </summary>
		public void SetData(object data)
		{
			Data = JsonSerializer.Serialize(data, new JsonSerializerOptions
			{
				WriteIndented = true
			});
		}
		
		/// <summary>
		/// Deserialize the Data property into an object of type T
		/// </summary>
		public T GetData<T>()
		{
			return string.IsNullOrEmpty(Data) ? default : JsonSerializer.Deserialize<T>(Data);
		}
	}
}