// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace ImpowerSurvey.Components.Model
{
	/// <summary>
	/// Base ServiceResult class that represents the outcome of a service operation
	/// </summary>
	public class ServiceResult
	{
		/// <summary>
		/// Indicates whether the operation was successful
		/// </summary>
		public bool Successful { get; set; }
		
		/// <summary>
		/// Message describing the outcome of the operation
		/// </summary>
		
		public string Message { get; set; }
		
		/// <summary>
		/// Creates a successful result with the specified message
		/// </summary>
		public static ServiceResult Success(string message)
		{
			return new ServiceResult { Successful = true, Message = message };
		}
		
		/// <summary>
		/// Creates a failure result with the specified message
		/// </summary>
		public static ServiceResult Failure(string message)
		{
			return new ServiceResult { Successful = false, Message = message };
		}
		
		/// <summary>
		/// Creates a successful result with data of type T
		/// </summary>
		public static DataServiceResult<T> Success<T>(T data, string message)
		{
			return new DataServiceResult<T> { Successful = true, Message = message, Data = data };
		}
		
		/// <summary>
		/// Creates a failure result with default value for type T
		/// </summary>
		public static DataServiceResult<T> Failure<T>(string message)
		{
			return new DataServiceResult<T> { Successful = false, Message = message, Data = default };
		}
	}
	
	/// <summary>
	/// Extended ServiceResult that includes data of type T
	/// </summary>
	public class DataServiceResult<T> : ServiceResult
	{
		/// <summary>
		/// The data returned by the service operation
		/// </summary>
		public T Data { get; set; }
	}
}