using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;

namespace ImpowerSurvey.Components.Layout;

public partial class MainLayout : IDisposable
{
	private bool _isVantaBackgroundEnabled;
	private bool IsSidebarExpanded { get; set; } = false;
	private string Icon { get; set; } = "stop_circle";
	private User User => CustomAuthStateProvider.Get(AuthStateProvider).GetUser();
	private bool IsDarkTheme => ThemeService.Theme == "material-dark";
	public Survey Survey { get; set; }
	
	private string LogoType { get; set; } = "svg";
	private string LogoSvg { get; set; }
	private string LogoUrl { get; set; }
	private string CompanyNameLogoType { get; set; } = "svg";
	private string CompanyNameLogoSvg { get; set; }
	private string CompanyNameLogoUrl { get; set; }

	private string SideBarStyle => IsDarkTheme ? string.Empty : "--impower-logo: black; --impower-logo-alt: var(--rz-primary);";
	private string HeaderStyle => IsDarkTheme ? string.Empty : "--impower-logo: black; --impower-logo-alt: white;";

	protected override async Task OnInitializedAsync()
	{
		await base.OnInitializedAsync();
		ThemeService.ThemeChanged += OnAppearanceToggle;
		AuthStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
		IsSidebarExpanded = await CustomAuthStateProvider.Get(AuthStateProvider).GetIsAuthenticatedAsync();
		await LoadLogoSettingsAsync();
	}

	private void OnAppearanceToggle()
	{
		_ = OnAppearanceToggleAsync();
	}

	private async Task OnAppearanceToggleAsync()
	{
		if (_isVantaBackgroundEnabled)
			await JSUtilityService.UpdateVantaForTheme(IsDarkTheme);

		await JSUtilityService.UpdateGlassCardStyles(IsDarkTheme);
		StateHasChanged();
	}

	private async Task LoadLogoSettingsAsync()
	{
		LogoType = await SettingsService.GetSettingValueAsync(Constants.SettingsKeys.CompanyLogoType, "svg");
		LogoSvg = await SettingsService.GetSettingValueAsync(Constants.SettingsKeys.CompanyLogoSvg, "");
		LogoUrl = await SettingsService.GetSettingValueAsync(Constants.SettingsKeys.CompanyLogoUrl, "/logo.png");
		
		CompanyNameLogoType = await SettingsService.GetSettingValueAsync(Constants.SettingsKeys.CompanyNameLogoType, "svg");
		CompanyNameLogoSvg = await SettingsService.GetSettingValueAsync(Constants.SettingsKeys.CompanyNameLogoSvg, "");
		CompanyNameLogoUrl = await SettingsService.GetSettingValueAsync(Constants.SettingsKeys.CompanyNameLogoUrl, "/company-name.png");
	}

	public void Dispose()
	{
		ThemeService.ThemeChanged -= OnAppearanceToggle;
		AuthStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
	}

	private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
	{
		_ = OnAuthenticationStateChangedAsync(task);
	}

	private async Task OnAuthenticationStateChangedAsync(Task<AuthenticationState> task)
	{
		IsSidebarExpanded = await CustomAuthStateProvider.Get(AuthStateProvider).GetIsAuthenticatedAsync();
		// UI needs to be updated since we're changing state from an event handler
		StateHasChanged();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		await CustomAuthStateProvider.Get(AuthStateProvider).ApplyDeferredAuthCookieAsync();
		
		if (firstRender)
		{
			var isMobile = await JSUtilityService.IsMobileDevice();
			if (isMobile)
			{
				NavigationManager.NavigateTo("/mobile");
				return;
			}

			await EnableVantaBackground(IsDarkTheme);
			await JSUtilityService.UpdateGlassCardStyles(IsDarkTheme);
			
			var tzCookie = await CookieService.GetAsync(Constants.App.TimeZoneCookieName);
			if (tzCookie == null)
			{
				var timeZone = await JSUtilityService.GetTimezone();
				await CookieService.SetAsync(Constants.App.TimeZoneCookieName, timeZone, DateTime.UtcNow.AddDays(30));
			}
			
			var cookieConsent = await CookieService.GetAsync(Constants.App.CookiesConsentCookieName);
			if (cookieConsent == null)
			{
				var result = await DialogService.OpenAsync<Controls.CookiePolicyDialog>("We use cookies", null,
					new DialogOptions
					{
						AutoFocusFirstElement = false,
						ShowTitle = true,
						ShowClose = true,
						CloseDialogOnEsc = true,
						CloseDialogOnOverlayClick = true
					});
					
				if (result ?? false)
					await CookieService.SetAsync(Constants.App.CookiesConsentCookieName, "1", DateTime.UtcNow.AddDays(30));
			}
		}
	}

	public void NavigateHome()
	{
		NavigationManager.NavigateTo("/");
	}

	private async Task EnableVantaBackground(bool isDarkTheme = true)
	{
		if (!_isVantaBackgroundEnabled)
		{
			await JSUtilityService.EnableVantaBackground(isDarkTheme);
			Icon = "stop_circle";
			_isVantaBackgroundEnabled = true;
		}
	}

	private async Task DisableVantaBackground()
	{
		if (_isVantaBackgroundEnabled)
		{
			await JSUtilityService.DisableVantaBackground();
			Icon = "animation";
			_isVantaBackgroundEnabled = false;
		}
	}

	private async Task ToggleVantaBackground()
	{
		if (_isVantaBackgroundEnabled)
			await DisableVantaBackground();
		else
			await EnableVantaBackground(IsDarkTheme);
	}

	private async Task HandleLogout()
	{
		var result = await DialogService.Confirm("Are you sure you want to log out?");
		if (result ?? false)
		{
			await CustomAuthStateProvider.Get(AuthStateProvider).LogoutAsync();
			NavigationManager.NavigateTo("/");
		}
	}

	private async Task SwitchToMobileVersion()
	{
		var result = await DialogService.Confirm("Are you sure you want to switch to the mobile version?<br/><br/>"
												 + "For the best experience, we recommend using the desktop version on any device larger than a phone (including tablets).");
		if (result ?? false)
			NavigationManager.NavigateTo("/mobile");
	}

	private async Task CreateExample()
	{
		if (User != null)
		{
			var result = await DialogService.Confirm(Constants.Survey.CreateExamplePrompt, "Create Example Survey", new ConfirmOptions
			{
				OkButtonText = "Yes",
				CancelButtonText = "No"
			});
			if (result ?? false)
			{
				await SurveyService.CreateExampleSurvey(User.Id);
				CustomAuthStateProvider.Get(AuthStateProvider).NotifyDataGridStateChanged();
			}
		}
	}
}