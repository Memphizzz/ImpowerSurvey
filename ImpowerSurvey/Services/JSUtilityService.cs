using Microsoft.JSInterop;

namespace ImpowerSurvey.Services;

/// <summary>
/// Service interface for JavaScript utility functions through JSInterop
/// </summary>
public interface IJSUtilityService
{
    /// <summary>
    /// Scrolls the page to a specific element by its ID
    /// </summary>
    /// <param name="elementId">The ID of the HTML element to scroll to</param>
    Task ScrollToElement(string elementId);
    
    /// <summary>
    /// Scrolls the page to the top
    /// </summary>
    Task ScrollToTop();
    
    /// <summary>
    /// Copies data to the system clipboard
    /// </summary>
    /// <param name="data">The data to copy to clipboard</param>
    Task CopyToClipboard(object data);
    
    /// <summary>
    /// Creates and downloads an HTML file with the specified content
    /// </summary>
    /// <param name="fileName">Name of the file to download</param>
    /// <param name="fileType">File extension/type</param>
    /// <param name="content">HTML content for the file</param>
    Task DownloadHtmlFile(string fileName, string fileType, string content);
    
    /// <summary>
    /// Prevents the browser tab from closing by showing a confirmation dialog
    /// </summary>
    /// <param name="message">Message to display in the confirmation dialog</param>
    Task PreventTabClosure(string message);
    
    /// <summary>
    /// Allows the browser tab to be closed without confirmation
    /// </summary>
    Task AllowTabClosure();
    
    /// <summary>
    /// Sets the primary and secondary Impower brand colors
    /// </summary>
    Task SetImpowerColors();
    
    /// <summary>
    /// Updates the Vanta.js background effects based on the current theme
    /// </summary>
    /// <param name="isDarkTheme">Whether the current theme is dark</param>
    Task UpdateVantaForTheme(bool isDarkTheme);
    
    /// <summary>
    /// Updates glass card styles based on the current theme
    /// </summary>
    /// <param name="isDarkTheme">Whether the current theme is dark</param>
    Task UpdateGlassCardStyles(bool isDarkTheme);
    
    /// <summary>
    /// Initializes the Vanta.js background effects
    /// </summary>
    /// <param name="isDarkTheme">Whether to use dark theme styling</param>
    Task EnableVantaBackground(bool isDarkTheme = true);
    
    /// <summary>
    /// Destroys the Vanta.js background effects to free resources
    /// </summary>
    Task DisableVantaBackground();
    
    /// <summary>
    /// Applies custom styling to tablist elements
    /// </summary>
    Task ApplyTabListStyle();
    
    /// <summary>
    /// Gets the user's timezone from the browser
    /// </summary>
    /// <returns>The user's timezone identifier</returns>
    Task<string> GetTimezone();
    
    /// <summary>
    /// Checks if the current device is a mobile device
    /// </summary>
    /// <returns>True if the device is mobile, false otherwise</returns>
    Task<bool> IsMobileDevice();
}

/// <summary>
/// Service implementation for JavaScript utility functions through JSInterop
/// </summary>
public class JSUtilityService : IJSUtilityService
{
    private readonly IJSRuntime _jsRuntime;
    
    /// <summary>
    /// Creates a new instance of JSUtilityService
    /// </summary>
    /// <param name="jsRuntime">The JSInterop runtime</param>
    public JSUtilityService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }
    
    /// <summary>
    /// Scrolls the page to a specific element by its ID
    /// </summary>
    /// <param name="elementId">The ID of the HTML element to scroll to</param>
    public async Task ScrollToElement(string elementId)
    {
        await _jsRuntime.InvokeVoidAsync("scrollToElement", elementId);
    }
    
    /// <summary>
    /// Scrolls the page to the top
    /// </summary>
    public async Task ScrollToTop()
    {
        await _jsRuntime.InvokeVoidAsync("scrollToTop");
    }
    
    /// <summary>
    /// Copies data to the system clipboard
    /// </summary>
    /// <param name="data">The data to copy to clipboard</param>
    public async Task CopyToClipboard(object data)
    {
        await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", data);
    }
    
    /// <summary>
    /// Creates and downloads an HTML file with the specified content
    /// </summary>
    /// <param name="fileName">Name of the file to download</param>
    /// <param name="fileType">File extension/type</param>
    /// <param name="content">HTML content for the file</param>
    public async Task DownloadHtmlFile(string fileName, string fileType, string content)
    {
        await _jsRuntime.InvokeVoidAsync("eval", $$"""
                                             var blob = new Blob([`{{content}}`], { type: 'text/html' });
                                             var url = URL.createObjectURL(blob);
                                             var link = document.createElement('a');
                                             link.href = url;
                                             link.download = '{{fileName}}.{{fileType}}';
                                             document.body.appendChild(link);
                                             link.click();
                                             document.body.removeChild(link);
                                             URL.revokeObjectURL(url);
                                         """);
    }
    
    /// <summary>
    /// Prevents the browser tab from closing by showing a confirmation dialog
    /// </summary>
    /// <param name="message">Message to display in the confirmation dialog</param>
    public async Task PreventTabClosure(string message)
    {
        await _jsRuntime.InvokeVoidAsync("preventWindowClose", message);
    }
    
    /// <summary>
    /// Allows the browser tab to be closed without confirmation
    /// </summary>
    public async Task AllowTabClosure()
    {
        await _jsRuntime.InvokeVoidAsync("allowWindowClose");
    }
    
    /// <summary>
    /// Sets the primary and secondary Impower brand colors
    /// </summary>
    public async Task SetImpowerColors()
    {
        await _jsRuntime.InvokeVoidAsync("setThemeColors", "#096AF2", "#F27A09");
    }
    
    /// <summary>
    /// Updates the Vanta.js background effects based on the current theme
    /// </summary>
    /// <param name="isDarkTheme">Whether the current theme is dark</param>
    public async Task UpdateVantaForTheme(bool isDarkTheme)
    {
        await _jsRuntime.InvokeVoidAsync("updateVantaForTheme", isDarkTheme);
    }
    
    /// <summary>
    /// Updates glass card styles based on the current theme
    /// </summary>
    /// <param name="isDarkTheme">Whether the current theme is dark</param>
    public async Task UpdateGlassCardStyles(bool isDarkTheme)
    {
        await _jsRuntime.InvokeVoidAsync("updateGlassCardStyles", isDarkTheme);
    }
    
    /// <summary>
    /// Initializes the Vanta.js background effects
    /// </summary>
    /// <param name="isDarkTheme">Whether to use dark theme styling</param>
    public async Task EnableVantaBackground(bool isDarkTheme = true)
    {
        await _jsRuntime.InvokeVoidAsync("initVantaBackground", "vanta-background", new
        {
            mouseControls = false,
            touchControls = false,
            gyroControls = false,
            isDarkTheme = isDarkTheme
        });
    }
    
    /// <summary>
    /// Destroys the Vanta.js background effects to free resources
    /// </summary>
    public async Task DisableVantaBackground()
    {
        await _jsRuntime.InvokeVoidAsync("destroyVantaBackground");
    }
    
    /// <summary>
    /// Applies custom styling to tablist elements
    /// </summary>
    public async Task ApplyTabListStyle()
    {
        var tablistElement = await _jsRuntime.InvokeAsync<IJSObjectReference>("document.querySelector", "ul[role='tablist']");
		if (tablistElement != null)
		{
			await _jsRuntime.InvokeVoidAsync("applyTablistStyle", tablistElement);
			await tablistElement.DisposeAsync();
		}
	}
    
    /// <summary>
    /// Gets the user's timezone from the browser
    /// </summary>
    /// <returns>The user's timezone identifier</returns>
    public async Task<string> GetTimezone()
    {
        return await _jsRuntime.InvokeAsync<string>("getTimezone");
    }
    
    /// <summary>
    /// Checks if the current device is a mobile device
    /// </summary>
    /// <returns>True if the device is mobile, false otherwise</returns>
    public async Task<bool> IsMobileDevice()
    {
        return await _jsRuntime.InvokeAsync<bool>("isMobileDevice");
    }
}