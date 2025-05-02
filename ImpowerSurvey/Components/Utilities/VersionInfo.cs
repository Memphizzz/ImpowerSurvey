using System.Reflection;

// Using root namespace for easier access across the application
// ReSharper disable once CheckNamespace
namespace ImpowerSurvey;

public static class VersionInfo
{
	private static readonly string CommitHash = Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA", EnvironmentVariableTarget.Process) ?? "dev";
	private static string ShortHash => CommitHash.Length > 7 ? CommitHash[..7] : CommitHash;
	private static readonly string Version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty;
	public static string FullVersion => $"{GetVersion(Version)}+{ShortHash}";
	private static string GetVersion(string v) => System.Version.TryParse(v, out var version) ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
}
