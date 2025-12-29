using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services.FeatureFlag;
using Blazor_Bedrock.Services.Auth;
using Microsoft.AspNetCore.Identity;
using MudBlazor;

namespace Blazor_Bedrock.Services.Navigation;

public interface IMenuService
{
    Task<List<MenuItem>> GetMenuItemsAsync(string userId, int? tenantId);
}

public class MenuService : IMenuService
{
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IPermissionService _permissionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MenuService(
        IFeatureFlagService featureFlagService,
        IPermissionService permissionService,
        UserManager<ApplicationUser> userManager)
    {
        _featureFlagService = featureFlagService;
        _permissionService = permissionService;
        _userManager = userManager;
    }

    public async Task<List<MenuItem>> GetMenuItemsAsync(string userId, int? tenantId)
    {
        var menuItems = new List<MenuItem>();
        var user = await _userManager.FindByIdAsync(userId);
        var isAdmin = user != null && await _userManager.IsInRoleAsync(user, "Admin");

        // Home
        menuItems.Add(new MenuItem
        {
            Title = "Home",
            Href = "/",
            Icon = Icons.Material.Filled.Home
        });

        // Settings Menu (Admin only)
        if (isAdmin && tenantId.HasValue)
        {
            var settingsMenu = new MenuItem
            {
                Title = "Settings",
                Icon = Icons.Material.Filled.Settings,
                Children = new List<MenuItem>()
            };

            settingsMenu.Children.Add(new MenuItem
            {
                Title = "Users",
                Href = "/admin/users",
                Icon = Icons.Material.Filled.People
            });

            settingsMenu.Children.Add(new MenuItem
            {
                Title = "Roles",
                Href = "/admin/roles",
                Icon = Icons.Material.Filled.Security
            });

            // Feature Settings (if enabled)
            if (await _featureFlagService.IsEnabledAsync("FeatureFlags_Enabled"))
            {
                settingsMenu.Children.Add(new MenuItem
                {
                    Title = "Feature Settings",
                    Href = "/admin/feature-settings",
                    Icon = Icons.Material.Filled.ToggleOn
                });
            }

            menuItems.Add(settingsMenu);
        }

        // ChatGPT Menu (if enabled)
        if (await _featureFlagService.IsEnabledAsync("ChatGpt_Enabled"))
        {
            var chatGptMenu = new MenuItem
            {
                Title = "ChatGPT",
                Icon = Icons.Material.Filled.Chat,
                Children = new List<MenuItem>()
            };

            chatGptMenu.Children.Add(new MenuItem
            {
                Title = "Settings",
                Href = "/chatgpt/settings",
                Icon = Icons.Material.Filled.Settings
            });

            chatGptMenu.Children.Add(new MenuItem
            {
                Title = "Prompts",
                Href = "/chatgpt/prompts",
                Icon = Icons.Material.Filled.Description
            });

            chatGptMenu.Children.Add(new MenuItem
            {
                Title = "Chat",
                Href = "/chatgpt/chat",
                Icon = Icons.Material.Filled.Forum
            });

            menuItems.Add(chatGptMenu);
        }

        // Stripe (if enabled)
        if (await _featureFlagService.IsEnabledAsync("Stripe_Enabled") && tenantId.HasValue)
        {
            menuItems.Add(new MenuItem
            {
                Title = "Payments",
                Href = "/stripe/subscriptions",
                Icon = Icons.Material.Filled.Payment
            });
        }

        // SuperAdmin - Migrations
        if (isAdmin && await _featureFlagService.IsEnabledAsync("Migrations_Enabled"))
        {
            menuItems.Add(new MenuItem
            {
                Title = "Migrations",
                Href = "/superadmin/migrations",
                Icon = Icons.Material.Filled.Storage
            });
        }

        return menuItems;
    }
}

public class MenuItem
{
    public string Title { get; set; } = string.Empty;
    public string? Href { get; set; }
    public string? Icon { get; set; }
    public List<MenuItem>? Children { get; set; }
}

