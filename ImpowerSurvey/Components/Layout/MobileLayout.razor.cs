using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;

namespace ImpowerSurvey.Components.Layout;

public partial class MobileLayout
{
	[Parameter]
	public string PageTitle { get; set; }

	[Parameter]
	public bool ShowRefresh { get; set; }

	[Parameter]
	public EventCallback OnRefresh { get; set; }

	[Parameter]
	public bool HideNavigation { get; set; }

	[Parameter]
	public RenderFragment Body { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

		// Subscribe to authentication state changes
		AuthStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await CustomAuthStateProvider.Get(AuthStateProvider).ApplyDeferredAuthCookieAsync();
        
        if (firstRender)
        {
			await JSUtilityService.EnableVantaBackground();
			await JSUtilityService.UpdateGlassCardStyles(true);

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
						ShowClose = true,
                        ShowTitle = true,
						Width = "100%",
                        Height = "100%",
						CssClass = "mobile-dialog",
						AutoFocusFirstElement = false,
						CloseDialogOnEsc = true,
						CloseDialogOnOverlayClick = true
                    });
                    
                if (result ?? false)
                    await CookieService.SetAsync(Constants.App.CookiesConsentCookieName, "1", DateTime.UtcNow.AddDays(30));
            }
        }
    }
    
    public void Dispose()
    {
        AuthStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
    
    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        _ = OnAuthenticationStateChangedAsync(task);
    }
    
    private async Task OnAuthenticationStateChangedAsync(Task<AuthenticationState> task)
    {
        // Force re-render when auth state changes
        await InvokeAsync(StateHasChanged);
    }
}