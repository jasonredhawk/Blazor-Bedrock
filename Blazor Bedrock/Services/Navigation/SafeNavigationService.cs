using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Blazor_Bedrock.Services.Navigation;

public interface ISafeNavigationService
{
    /// <summary>
    /// Safely navigates to the specified URI, catching and logging any NavigationException.
    /// </summary>
    /// <param name="uri">The destination URI</param>
    /// <param name="forceLoad">If true, forces a full page reload</param>
    /// <param name="replace">If true, replaces the current entry in the history stack</param>
    /// <returns>True if navigation succeeded, false if it failed</returns>
    bool NavigateTo(string uri, bool forceLoad = false, bool replace = false);
    
    /// <summary>
    /// Safely navigates to the specified URI asynchronously, catching and logging any NavigationException.
    /// </summary>
    /// <param name="uri">The destination URI</param>
    /// <param name="forceLoad">If true, forces a full page reload</param>
    /// <param name="replace">If true, replaces the current entry in the history stack</param>
    /// <returns>True if navigation succeeded, false if it failed</returns>
    Task<bool> NavigateToAsync(string uri, bool forceLoad = false, bool replace = false);
}

public class SafeNavigationService : ISafeNavigationService
{
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<SafeNavigationService>? _logger;

    public SafeNavigationService(NavigationManager navigationManager, ILogger<SafeNavigationService>? logger = null)
    {
        _navigationManager = navigationManager;
        _logger = logger;
    }

    public bool NavigateTo(string uri, bool forceLoad = false, bool replace = false)
    {
        try
        {
            _navigationManager.NavigateTo(uri, forceLoad, replace);
            return true;
        }
        catch (Microsoft.AspNetCore.Components.NavigationException ex)
        {
            _logger?.LogWarning(ex, "Navigation failed to {Uri}: {Message}", uri, ex.Message);
            // Try to navigate to a safe fallback location
            try
            {
                _navigationManager.NavigateTo("/", forceLoad: true);
            }
            catch
            {
                // If even the fallback fails, log it but don't throw
                _logger?.LogError("Failed to navigate to fallback location");
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during navigation to {Uri}", uri);
            return false;
        }
    }

    public Task<bool> NavigateToAsync(string uri, bool forceLoad = false, bool replace = false)
    {
        // NavigationManager.NavigateTo is synchronous, so we just wrap the synchronous call
        return Task.FromResult(NavigateTo(uri, forceLoad, replace));
    }
}
