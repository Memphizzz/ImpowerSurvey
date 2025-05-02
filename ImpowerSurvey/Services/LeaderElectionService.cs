using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using System.Globalization;

namespace ImpowerSurvey.Services
{
	/// <summary>
	/// Implementation of leader election service that uses settings database for coordination
	/// between multiple application instances, with heartbeat-based fail-over.
	/// Can also operate in SingleInstanceMode to reduce database access.
	/// </summary>
	public class LeaderElectionService(ISettingsService settingsService, ILogService logService, IHttpClientFactory httpClientFactory) : ILeaderElectionService, IHostedService, IDisposable
	{
		public static readonly string InstanceId =
			$"{Utilities.GetEnvVar(Constants.App.EnvHostname, "HOSTNAME")}:{Utilities.GetEnvVar(Constants.App.EnvPort, "PORT")}";

		private static readonly bool IsSingleInstanceMode =
			string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.App.EnvScaleOut, EnvironmentVariableTarget.Process)) ||
			!bool.TryParse(Environment.GetEnvironmentVariable(Constants.App.EnvScaleOut, EnvironmentVariableTarget.Process), out var isScaleOut) ||
			!isScaleOut;

		// WARNING: DO NOT CONVERT TO PRIMARY CONSTRUCTOR PARAMETERS!
		// This is a technical limitation with C# primary constructors when implementing interfaces:
		// 1. Primary constructor parameters are only correctly accessible from methods declared in the interface
		//    (e.g., StartAsync and StopAsync from IHostedService)
		// 2. When called from non-interface methods, the compiler-generated backing fields will be null at runtime
		// 3. This causes NullReferenceExceptions despite the code appearing syntactically correct
		// ReSharper incorrectly suggests converting these fields to primary constructor parameters, but doing so
		// would break functionality in all methods that aren't part of IHostedService.

		// ReSharper disable ReplaceWithPrimaryConstructorParameter
		private readonly ILogService _logService = logService;
		private readonly ISettingsService _settingsService = settingsService;
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
		// ReSharper restore ReplaceWithPrimaryConstructorParameter

		public bool IsLeader { get; private set; }
		public bool IsReady { get; private set; }

		private TimeSpan _leaderTimeout;
		private TimeSpan _leaderCheckInterval;
		private Timer _leaderElectionTimer;
		private string _acknowledgedLeaderId;

		string ILeaderElectionService.InstanceId => InstanceId;
		public event Action<bool> OnLeadershipChanged;

		/// <summary>
		/// Starts the leader election process by initializing settings and beginning periodic leadership checks.
		/// In SingleInstanceMode, this instance is automatically the leader without performing election.
		/// </summary>
		/// <param name="cancellationToken">Token to monitor for cancellation requests</param>
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			if (IsSingleInstanceMode)
			{
				await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
										   $"Starting LeaderElectionService in SingleInstanceMode. This instance ({InstanceId}) is the leader.");

				SetLeaderStatus(true);
				IsReady = true;
				return;
			}

			var leaderTimeoutMinutes = await _settingsService.GetIntSettingAsync(Constants.SettingsKeys.LeaderTimeout, 2);
			_leaderTimeout = TimeSpan.FromMinutes(leaderTimeoutMinutes);

			var checkIntervalSeconds = await _settingsService.GetIntSettingAsync(Constants.SettingsKeys.LeaderCheckIntervalSeconds, 120);
			_leaderCheckInterval = TimeSpan.FromSeconds(checkIntervalSeconds);

			await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
									   $"Starting LeaderElectionService with instance ID: {InstanceId}, check interval: {checkIntervalSeconds}s");

			// Verify inter-instance communication if this instance is a follower
			var leaderInstanceId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
			if (!string.IsNullOrWhiteSpace(leaderInstanceId) && InstanceId != leaderInstanceId)
			{
				await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
										   $"Verifying inter-instance communication with leader {leaderInstanceId}");

				var leaderUrl = $"https://{leaderInstanceId}";
				var instanceSecret = Environment.GetEnvironmentVariable(Constants.App.EnvInstanceSecret, EnvironmentVariableTarget.Process);

				var testResult = await _httpClientFactory.VerifyInstanceCommunicationAsync(leaderUrl, InstanceId, instanceSecret);

				if (!testResult.Successful)
				{
					var errorMessage = $"Inter-instance communication test failed: {testResult.Message}. Instance cannot start safely as responses would get lost.";
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Error, errorMessage);
					throw new InvalidOperationException(errorMessage);
				}

				await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
										   "Inter-instance communication verified successfully");
			}

			// Start the leadership check timer
			_leaderElectionTimer = new Timer(CheckLeadershipAsync, null, TimeSpan.Zero, _leaderCheckInterval);
		}

		/// <summary>
		/// Stops the leader election process and relinquishes leadership if this instance is the leader
		/// </summary>
		/// <param name="cancellationToken">Token to monitor for cancellation requests</param>
		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_leaderElectionTimer?.Change(Timeout.Infinite, 0);

			// If single instance mode, no need to relinquish leadership
			if (IsSingleInstanceMode)
				return;

			// If this instance is the leader, clear the leader ID so another instance can take over
			if (IsLeader)
				await RelinquishLeadershipAsync();
		}

		/// <summary>
		/// Disposes of the leadership timer resources
		/// </summary>
		public void Dispose()
		{
			_leaderElectionTimer?.Dispose();
			_leaderElectionTimer = null;
		}

		/// <summary>
		/// Gives up leadership by clearing leader ID in settings if this instance is currently the leader
		/// </summary>
		private async Task RelinquishLeadershipAsync()
		{
			try
			{
				var currentLeaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);

				// Only clear if this instance is still the leader
				if (currentLeaderId == InstanceId)
				{
					await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderId, string.Empty);
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
											   $"Instance {InstanceId} has relinquished leadership");
				}

				SetLeaderStatus(false);
			}
			catch (Exception ex)
			{
				await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Error,
										   $"Error relinquishing leadership for instance {InstanceId}: {ex.Message}");
			}
		}

		/// <summary>
		/// Timer callback that performs leadership checks and election.
		/// Not used in SingleInstanceMode.
		/// </summary>
		/// <param name="state">Timer state object (not used)</param>
		private async void CheckLeadershipAsync(object state)
		{
			if (IsSingleInstanceMode)
				return;

			try
			{
				var currentLeaderId = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderId);
				var heartbeat = await _settingsService.GetSettingValueAsync(Constants.SettingsKeys.LeaderHeartbeat);

				// If this instance is already the leader, update the heartbeat
				if (currentLeaderId == InstanceId)
				{
					await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderHeartbeat, DateTime.UtcNow.ToString("o"));
					if (!IsLeader)
					{
						SetLeaderStatus(true);
						await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
												   $"Instance {InstanceId} confirming leadership status");
					}

					return;
				}

				// If there's no current leader, try to become the leader
				if (string.IsNullOrEmpty(currentLeaderId))
				{
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
											   "No active leader found. Attempting to claim leadership");

					// Use conditional update to safely attempt leadership claim
					var claimed = await _settingsService.TryUpdateSettingWithConditionAsync(Constants.SettingsKeys.LeaderId, InstanceId, string.IsNullOrEmpty);

					if (claimed)
					{
						await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderHeartbeat, DateTime.UtcNow.ToString("o"));
						SetLeaderStatus(true);
						await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
												   $"Instance {InstanceId} has been elected as the new leader");
					}
					else if (IsLeader)
						SetLeaderStatus(false);

					return;
				}

				// Parse the ISO string through DateTimeOffset which properly handles timezone info
				var parsedDate = DateTimeOffset.Parse(heartbeat, CultureInfo.InvariantCulture);
				var lastHeartbeat = new DateTime(parsedDate.Ticks, DateTimeKind.Utc);

				// There is a leader, check if it's expired
				var leaderExpired = DateTime.UtcNow - lastHeartbeat > _leaderTimeout;
				if (leaderExpired)
				{
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Warning,
											   $"Previous leader {currentLeaderId} has expired (last heartbeat: {lastHeartbeat:u} UTC, current time: {DateTime.UtcNow:u} UTC)");

					// Try to take over as leader
					var claimed = await _settingsService.TryUpdateSettingWithConditionAsync(Constants.SettingsKeys.LeaderId, InstanceId, current => current == currentLeaderId);

					if (claimed)
					{
						await _settingsService.UpdateSettingAsync(Constants.SettingsKeys.LeaderHeartbeat, DateTime.UtcNow.ToString("o"));
						SetLeaderStatus(true);
						await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
												   $"Instance {InstanceId} has taken over from expired leader");
					}
					else if (IsLeader)
						SetLeaderStatus(false);
				}
				else if (IsLeader)
				{
					// We thought we were leader, but there's another active leader
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Warning,
											   $"Instance {InstanceId} was leader but has been superseded by {currentLeaderId}");
					SetLeaderStatus(false);
				}
				else if (_acknowledgedLeaderId != currentLeaderId)
				{
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information, $"Accepting {currentLeaderId} as current Leader.");
					_acknowledgedLeaderId = currentLeaderId;
				}
			}
			catch (Exception ex)
			{
				await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Error,
										   $"Error during leader election for instance: {ex.Message}");
			}
			finally
			{
				if (!IsReady)
				{
					IsReady = true;
					await _logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
											   "LeaderElectionService initialization completed");
				}
			}
		}

		/// <summary>
		/// Updates the leader status and triggers the leadership changed event if the status has changed
		/// </summary>
		/// <param name="isLeader">The new leadership status</param>
		private void SetLeaderStatus(bool isLeader)
		{
			if (IsLeader != isLeader)
			{
				IsLeader = isLeader;

				// Reset the acknowledged leader ID when this instance becomes leader
				if (isLeader)
					_acknowledgedLeaderId = null;

				OnLeadershipChanged?.Invoke(IsLeader);
			}
		}
	}
}
