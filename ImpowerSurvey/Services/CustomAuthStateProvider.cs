using BitzArt.Blazor.Cookies;
using ImpowerSurvey.Components.Model;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ImpowerSurvey.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider, IDisposable
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
	private readonly SurveyCodeService _surveyCodeService;
	private readonly UserService _userService;
	private readonly ICookieService _cookieService;
	private readonly IConfiguration _configuration;
	private readonly ILogService _logService;
	private readonly byte[] _jwtKey;
	// ReSharper restore ReplaceWithPrimaryConstructorParameter

	public User GetUser() => _currentUser;
	public Survey GetCurrentSurvey() => _survey;
	public int GetCurrentPageIndex() => _currentPageIndex;
	public ResultsTab GetCurrentResultsTab() => _currentResultsTab;
	public int GetCurrentQuestionId() => _currentQuestionId;
	public TimeZoneInfo GetUserTimeZone() => _userTimeZone ?? TimeZoneInfo.Utc;

	public event Action<Survey, int> SurveyNavigationStateChanged;
	public event Action DataGridStateChanged;
	public event Action<ResultsTab, int> ResultsTabChanged;
	public event Func<SurveyEditorAction, object, Task> OnSurveyEditorAction;

	private int _currentPageIndex;
	private int _currentQuestionId = -1;
	private ResultsTab _currentResultsTab = ResultsTab.Overview;

	private ClaimsPrincipal _currentPrincipal;
	private Survey _survey;
	private User _currentUser;
	private TimeZoneInfo _userTimeZone;
	private Guid? _pendingPasswordChangeUserId;
	private DateTime _lastLogoutTime = DateTime.MinValue;
	private string _deferredAuthCookie;

	public Guid? GetPendingPasswordChangeUserId() => _pendingPasswordChangeUserId;
	public void ClearPendingPasswordChangeUserId() => _pendingPasswordChangeUserId = null;

	public static CustomAuthStateProvider Get(AuthenticationStateProvider provider) => (CustomAuthStateProvider)provider;

	public CustomAuthStateProvider(UserService userService, SurveyCodeService surveyCodeService, ICookieService cookieService, IConfiguration configuration, ILogService logService)
	{
		_userService = userService;
		_surveyCodeService = surveyCodeService;
		_cookieService = cookieService;
		_configuration = configuration;
		_logService = logService;

		var key = Environment.GetEnvironmentVariable(Constants.App.EnvCookieSecret, EnvironmentVariableTarget.Process) ??
				  _configuration[Constants.App.EnvCookieSecret] ??
				  throw new InvalidOperationException("JWT secret key not configured");
		_jwtKey = Encoding.UTF8.GetBytes(key);
	}

	public override async Task<AuthenticationState> GetAuthenticationStateAsync()
	{
		if (_currentPrincipal != null)
			return new AuthenticationState(_currentPrincipal);

		// Try to restore from cookie
		var cookie = await _cookieService.GetAsync(Constants.App.AuthCookieName);
		if (cookie != null)
		{
			var result = await ValidateToken(cookie.Value);
			if (result.Success)
			{
				await SetupAuthenticatedUser(result.User, false);
				return new AuthenticationState(_currentPrincipal!);
			}
		}

		return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
	}

	private async Task SetupAuthenticatedUser(User user, bool notify = true)
	{
		if (_currentPrincipal?.Identity is { IsAuthenticated: true })
			return;

		var claims = new List<Claim> { new(ClaimTypes.Name, user.Username), new(ClaimTypes.Role, user.Role.ToString()) };

		var identity = new ClaimsIdentity(claims, "Server authentication");
		_currentPrincipal = new ClaimsPrincipal(identity);
		_currentUser = user;

		if (string.IsNullOrWhiteSpace(user.TimeZone))
		{
			var tzCookie = await _cookieService.GetAsync(Constants.App.TimeZoneCookieName);
			if (tzCookie != null)
			{
				if (user.TimeZone != tzCookie.Value)
					await _userService.UpdateTimeZone(user.Id, tzCookie.Value);

				user.TimeZone = tzCookie.Value;
			}
		}

		if (user.TimeZone != null && TimeZoneInfo.TryFindSystemTimeZoneById(user.TimeZone, out var timeZoneInfo))
			_userTimeZone = timeZoneInfo;
		else
			_userTimeZone = TimeZoneInfo.Utc;

		if (notify)
			NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
	}

	public async Task<DataServiceResult<string>> LoginAsync(string username, string password, bool isMobileLayout = false)
	{
		var user = await _userService.GetUserAsync(username);
		if (user == null)
		{
			await _logService.LogAsync(LogSource.AuthService, LogLevel.Warning, $"Invalid login attempt: Username not found ({username})", containsIdentityData: true);
			return ServiceResult.Failure<string>(Constants.Auth.InvalidLogin);
		}
		
		if (!VerifyPassword(password, user.PasswordHash))
		{
			await _logService.LogAsync(LogSource.AuthService, LogLevel.Warning, $"Invalid login attempt: Incorrect password for user {user.Username} (ID: {user.Id})", containsIdentityData: true);
			return ServiceResult.Failure<string>(Constants.Auth.InvalidLogin);
		}

		// Check if password change is required
		if (user.RequirePasswordChange)
		{
			// Store the user ID for the password change page
			_pendingPasswordChangeUserId = user.Id;
			return ServiceResult.Success("/change-password", Constants.Users.PasswordChangeRequired);
		}

		// Set up auth state in memory first
		await SetupAuthenticatedUser(user);
		
		// Generate token but don't set cookie yet - store for later
		_deferredAuthCookie = GenerateToken(user);
		await _logService.LogAsync(LogSource.AuthService, LogLevel.Information, $"User logged in: {user.Username} (ID: {user.Id})");

		var url = Constants.UI.GetLoginUrlForRole(user.Role, isMobileLayout);
		return ServiceResult.Success(url, "Login successful");
	}

	public async Task<DataServiceResult<Guid>> LoginSurveyParticipantAsync(string entryCode)
	{
		var result = await _surveyCodeService.ValidateEntryCodeAsync(entryCode);
		if (result.Successful)
		{
			var claims = new[] { 
				new Claim(ClaimTypes.Name, nameof(Roles.SurveyParticipant)), 
				new Claim(ClaimTypes.Role, nameof(Roles.SurveyParticipant)), 
				new Claim(nameof(EntryCode), entryCode)
			};
			var identity = new ClaimsIdentity(claims, "Server authentication");
			_currentPrincipal = new ClaimsPrincipal(identity);
			_currentUser = null;

			NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
		}
		else
		{
			await _logService.LogAsync(LogSource.AuthService, LogLevel.Warning, $"Invalid survey entry code attempt", 
				data: new { EntryCode = entryCode });
		}

		return result;
	}

	public async Task LogoutAsync()
	{
		_currentUser = null;
		_currentPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
		_survey = null;
		_currentPageIndex = 0;
		_lastLogoutTime = DateTime.UtcNow;

		await _cookieService.RemoveAsync(Constants.App.AuthCookieName);
		NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
	}
	
	/// <summary>
	/// Checks if the user logged out recently (within the last 5 seconds)
	/// </summary>
	public bool WasRecentlyLoggedOut()
	{
		return (DateTime.UtcNow - _lastLogoutTime).TotalSeconds <= 5;
	}
	
	/// <summary>
	/// Applies any pending authentication cookie during component render cycle.
	/// Only applies to SurveyManagers or Administrators.
	/// </summary>
	public async Task ApplyDeferredAuthCookieAsync()
	{
		if (_currentUser == null || string.IsNullOrEmpty(_deferredAuthCookie)) 
			return;
			
		var token = _deferredAuthCookie;
		_deferredAuthCookie = null;
		
		await _cookieService.SetAsync(Constants.App.AuthCookieName, token, DateTime.UtcNow.AddDays(30));
	}

	private string GenerateToken(User user)
	{
		var tokenHandler = new JwtSecurityTokenHandler();
		var tokenDescriptor = new SecurityTokenDescriptor
		{
			Subject = new ClaimsIdentity([new Claim(nameof(User.Id), user.Id.ToString())]),
			Expires = DateTime.UtcNow.AddDays(30),
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_jwtKey), SecurityAlgorithms.HmacSha256Signature)
		};

		var token = tokenHandler.CreateToken(tokenDescriptor);
		return tokenHandler.WriteToken(token);
	}

	private async Task<(bool Success, User User)> ValidateToken(string token)
	{
		var tokenHandler = new JwtSecurityTokenHandler();
		try
		{
			var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
			{
				ValidateIssuerSigningKey = true,
				IssuerSigningKey = new SymmetricSecurityKey(_jwtKey),
				ValidateIssuer = false,
				ValidateAudience = false,
				ValidateLifetime = true,
				ClockSkew = TimeSpan.FromMinutes(5) // Allow 5 minute tolerance for server clock differences
			}, out var _);

			// Get ID from token and fetch user from DB
			var userIdClaim = principal.FindFirst(nameof(User.Id))?.Value;
			if (Guid.TryParse(userIdClaim, out var userId))
			{
				var user = await _userService.GetUserAsync(userId);
				return (user != null, user);
			}

			await _logService.LogAsync(LogSource.AuthService, LogLevel.Warning, $"JWT token invalid for userIdClaim: {userIdClaim}");
			return (false, null);
		}
		catch (Exception ex)
		{
			await _logService.LogAsync(LogSource.AuthService, LogLevel.Warning, $"JWT token validation failed: {ex.Message}");
			return (false, null);
		}
	}

	private static bool VerifyPassword(string password, string storedHash) => BCrypt.Net.BCrypt.Verify(password, storedHash);

	public async Task<bool> GetIsAuthenticatedAsync()
	{
		var authState = await GetAuthenticationStateAsync();
		return authState.User.Identity?.IsAuthenticated ?? false;
	}

	#region UI
	public async Task RequestEditorAction(SurveyEditorAction action, object parameter = null)
	{
		if (OnSurveyEditorAction != null)
			await OnSurveyEditorAction.Invoke(action, parameter);
	}

	private void NotifySurveyNavigationStateChanged()
	{
		SurveyNavigationStateChanged?.Invoke(_survey, _currentPageIndex);
	}

	public void NotifyDataGridStateChanged()
	{
		DataGridStateChanged?.Invoke();
	}

	public void SetCurrentSurvey(Survey survey)
	{
		_survey = survey;
		NotifySurveyNavigationStateChanged();
	}

	public void SetCurrentPageIndex(int pageIndex)
	{
		_currentPageIndex = pageIndex;
		NotifySurveyNavigationStateChanged();
	}

	public void SetCurrentResultsTab(ResultsTab tab, int questionId = -1)
	{
		_currentResultsTab = tab;

		if (tab == ResultsTab.Question && questionId >= 0)
			_currentQuestionId = questionId;
		else if (tab != ResultsTab.Question)
			_currentQuestionId = -1; // Reset question ID when not on a question tab

		ResultsTabChanged?.Invoke(_currentResultsTab, _currentQuestionId);
	}

	public DateTime GetLocalNow()
	{
		var result = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetUserTimeZone()), DateTimeKind.Local);
		return result;
	}

	public DateTime ToLocal(DateTime dt)
	{
		if (dt.Kind != DateTimeKind.Utc)
			dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

		var result = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(dt, GetUserTimeZone()), DateTimeKind.Local);
		return result;
	}

	public DateTime ToUtc(DateTime dt)
	{
		var result = TimeZoneInfo.ConvertTimeToUtc(new DateTime(dt.Ticks, DateTimeKind.Unspecified), GetUserTimeZone());
		return result;
	}
	#endregion

	public void Dispose()
	{
		SurveyNavigationStateChanged = null;
		DataGridStateChanged = null;
		ResultsTabChanged = null;
		OnSurveyEditorAction = null;
	}
}
