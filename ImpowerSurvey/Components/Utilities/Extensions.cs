using Radzen;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace ImpowerSurvey.Components.Utilities;

public static class Extensions
{
	private static readonly Regex EmailRegex = new(@"([a-z0-9][-a-z0-9_\+\.]*[a-z0-9])@([a-z0-9][-a-z0-9\.]*[a-z0-9]\.\S{2,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public static bool IsValidEmail(this string email) => !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);

	public static string GetEnumDescription(this Enum value)
	{
		var fi = value.GetType().GetField(value.ToString());
		if (fi?.GetCustomAttributes(typeof(DescriptionAttribute), false) is DescriptionAttribute[] attributes && attributes.Any())
			return attributes.First().Description;

		return value.ToString();
	}
	
	public static void NotifyFromServiceResult(this NotificationService service, ServiceResult result, double? duration = null)
	{
		service.Notify(
			result.Successful ? NotificationSeverity.Success : NotificationSeverity.Error,
			result.Successful ? Constants.UI.Success : Constants.UI.Error,
			result.Message ?? string.Empty,
			duration ?? 4000);
	}
	
	public static void NotifyFromServiceResult<T>(this NotificationService service, DataServiceResult<T> result, double? duration = null)
	{
		service.Notify(
			result.Successful ? NotificationSeverity.Success : NotificationSeverity.Error,
			result.Successful ? Constants.UI.Success : Constants.UI.Error,
			result.Message ?? string.Empty,
			duration ?? 4000);
	}

	public static void NotifyFromException(this NotificationService service, Exception ex)
	{
		NotifyFromServiceResult(service, ServiceResult.Failure<string>(ex.Message));
	}

	public static DateTime GetLocalNow(this AuthenticationStateProvider provider) => ((CustomAuthStateProvider)provider).GetLocalNow();
	public static DateTime ToLocal(this AuthenticationStateProvider provider, DateTime dt) => ((CustomAuthStateProvider)provider).ToLocal(dt);
	public static DateTime ToUtc(this AuthenticationStateProvider provider, DateTime dt) => ((CustomAuthStateProvider)provider).ToUtc(dt);
	public static TimeZoneInfo GetLocalTimeZone(this AuthenticationStateProvider provider) => ((CustomAuthStateProvider)provider).GetUserTimeZone();
	public static DateTime ToProviderLocal(this DateTime dt, AuthenticationStateProvider provider) => provider.ToLocal(dt);
	public static DateTime ToProviderUtc(this DateTime dt, AuthenticationStateProvider provider) => provider.ToUtc(dt);
	
	public static string GetPrimaryActionText(this Survey survey)
	{
		return survey.State switch
		{
			SurveyStates.Created or SurveyStates.Scheduled => "Edit",
			SurveyStates.Running => "View Participants",
			SurveyStates.Closed => "View Results",
			var _ => "View"
		};
	}
	
	public static string GetPrimaryActionIcon(this Survey survey)
	{
		return survey.State switch
		{
			SurveyStates.Created or SurveyStates.Scheduled => "edit",
			SurveyStates.Running => "groups",
			SurveyStates.Closed => "monitoring",
			var _ => "info"
		};
	}
	
	public static BadgeStyle GetSurveyStateBadgeStyle(this SurveyStates state)
	{
		return state switch
		{
			SurveyStates.Created => BadgeStyle.Info,
			SurveyStates.Scheduled => BadgeStyle.Secondary,
			SurveyStates.Running => BadgeStyle.Success,
			SurveyStates.Closed => BadgeStyle.Light,
			var _ => BadgeStyle.Primary
		};
	}

	public static string GetIdBlock(this Guid guid)
	{
		var guidString = guid.ToString();
		var midpoint = guidString.Length / 2;
		return guidString[..midpoint] + "<br>" + guidString[midpoint..];
	}
	
	/// <summary>
	/// Converts a GUID to a ridiculously funny human-readable identifier
	/// </summary>
	public static string ToHumanReadableId(this Guid guid)
	{
		var adjectives = new[] { 
			"deranged", "bamboozled", "unhinged", "flabbergasted", "discombobulated", 
			"baffled", "rattled", "crazed", "perplexed", "bonkers", 
			"ballistic", "befuddled", "dumbfounded", "ludicrous", "maniacal",
			"hysterical", "delusional", "wonky", "neurotic", "absurd"
		};
    
		var nouns = new[] { 
			"waffle", "pancake", "toupee", "doorknob", "teacup", "pickle", "toaster", 
			"pogo-stick", "banjo", "spatula", "taco", "jellybean", "blender", "kazoo", 
			"meatball", "llama", "potato", "tuba", "dodo", "moustache"
		};
    
		var descriptors = new[] {
			"fiasco", "catastrophe", "conundrum", "malfunction", "debacle", 
			"predicament", "incident", "cataclysm", "shenanigans", "pandemonium", 
			"apocalypse", "meltdown", "nonsense", "disaster", "mayhem",
			"explosion", "emergency", "phenomenon", "tsunami", "breakdown"
		};
    
		var bytes = guid.ToByteArray();
		
		var adjIndex = ((bytes[0] << 8) | bytes[1]) % adjectives.Length;
		var nounIndex = ((bytes[2] << 8) | bytes[3]) % nouns.Length; 
		var descIndex = ((bytes[4] << 8) | bytes[5]) % descriptors.Length;
    
		return $"{adjectives[adjIndex]}-{nouns[nounIndex]}-{descriptors[descIndex]}";
	}
}

public static class Utilities
{
	public static string GetEnvVar(string primary, string fallback)
	{
		var value = Environment.GetEnvironmentVariable(primary, EnvironmentVariableTarget.Process);
		if (string.IsNullOrEmpty(value))
			value = Environment.GetEnvironmentVariable(fallback, EnvironmentVariableTarget.Process);

		return value ?? string.Empty;
	}

	private static string GetTimeBasedGreeting(DateTime? dateTime = null)
	{
		// Use provided datetime or current time
		var time = dateTime ?? DateTime.UtcNow;
		var hour = time.Hour;

		return hour switch
		{
			>= 5 and < 12  => "Good morning",
			>= 12 and < 17 => "Good afternoon",
			>= 17 and < 21 => "Good evening",
			var _          => "Good night"
		};
	}
	
	/// <summary>
	/// Gets a time-appropriate greeting based on a user's timezone
	/// </summary>
	/// <param name="timeZone">The timezone info (optional)</param>
	/// <param name="logService">Optional log service for error logging</param>
	/// <returns>Time-appropriate greeting ("Good morning", "Good afternoon", etc.)</returns>
	public static string GetTimezoneAwareGreeting(string timeZone, ILogService logService)
	{
		try
		{
			// Try to use the manager's timezone if provided
			if (!string.IsNullOrEmpty(timeZone))
			{
				if (TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var managerTimezone))
				{
					var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, managerTimezone);
					return GetTimeBasedGreeting(localTime);
				}
			}
			
			// Fall back to UTC
			return GetTimeBasedGreeting(DateTime.UtcNow);
		}
		catch (Exception ex)
		{
			logService?.LogAsync(LogSource.SlackService, LogLevel.Warning, 
				$"Error getting timezone for manager using TimeZone {timeZone}: {ex.Message}").GetAwaiter().GetResult();
			return GetTimeBasedGreeting(DateTime.UtcNow);
		}
	}
}
