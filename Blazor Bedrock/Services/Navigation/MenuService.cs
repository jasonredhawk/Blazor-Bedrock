using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services.FeatureFlag;
using Blazor_Bedrock.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    private readonly IDatabaseSyncService _dbSync;
    private readonly ApplicationDbContext _context;

    public MenuService(
        IFeatureFlagService featureFlagService,
        IPermissionService permissionService,
        UserManager<ApplicationUser> userManager,
        IDatabaseSyncService dbSync,
        ApplicationDbContext context)
    {
        _featureFlagService = featureFlagService;
        _permissionService = permissionService;
        _userManager = userManager;
        _dbSync = dbSync;
        _context = context;
    }

    public async Task<List<MenuItem>> GetMenuItemsAsync(string userId, int? tenantId)
    {
        // Synchronize all database operations to prevent concurrent access
        return await _dbSync.ExecuteAsync(async () =>
        {
            var menuItems = new List<MenuItem>();
            
            // Check if user is Admin for the current tenant (tenant-specific check)
            var isAdmin = false;
            if (tenantId.HasValue)
            {
                // Check if user has Admin role for this specific tenant
                var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
                if (adminRole != null)
                {
                    isAdmin = await _context.UserRoles
                        .AnyAsync(ur => ur.UserId == userId && ur.RoleId == adminRole.Id && ur.TenantId == tenantId.Value);
                }
            }
            
            // Also check global Admin role as fallback
            if (!isAdmin)
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                }
            }

            // Home - always show
            menuItems.Add(new MenuItem
            {
                Title = "Home",
                Href = "/",
                Icon = Icons.Material.Filled.Home
            });

            // If no tenant, only show Home menu
            if (!tenantId.HasValue)
            {
                return menuItems;
            }

            // Settings Menu (Permission-based, with Admin fallback)
            if (tenantId.HasValue)
            {
                var settingsMenu = new MenuItem
                {
                    Title = "Settings",
                    Icon = Icons.Material.Filled.Settings,
                    Children = new List<MenuItem>()
                };

                // If Admin, show all menu items without permission checks
                if (isAdmin)
                {
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
                }
                else
                {
                    // Non-admin users: check permissions
                    var canViewUsers = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "Users.View");
                    if (canViewUsers)
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Users",
                            Href = "/admin/users",
                            Icon = Icons.Material.Filled.People
                        });
                    }

                    var canViewRoles = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "Roles.View");
                    if (canViewRoles)
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Roles",
                            Href = "/admin/roles",
                            Icon = Icons.Material.Filled.Security
                        });
                    }
                }

                // Only add Settings menu if it has children
                if (settingsMenu.Children.Any())
                {
                    menuItems.Add(settingsMenu);
                }
            }
            // ChatGPT Menu (Permission-based, requires tenant)
            if (tenantId.HasValue && await _featureFlagService.IsEnabledAsync("ChatGpt_Enabled"))
            {
                var chatGptMenu = new MenuItem
                {
                    Title = "ChatGPT",
                    Icon = Icons.Material.Filled.Chat,
                    Children = new List<MenuItem>()
                };

                // If Admin, show all ChatGPT menu items
                if (isAdmin)
                {
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
                }
                else
                {
                    // Non-admin users: check permissions
                    var canViewChatGpt = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "ChatGpt.View");
                    if (canViewChatGpt)
                    {
                        var canAccessSettings = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "ChatGpt.Settings");
                        if (canAccessSettings)
                        {
                            chatGptMenu.Children.Add(new MenuItem
                            {
                                Title = "Settings",
                                Href = "/chatgpt/settings",
                                Icon = Icons.Material.Filled.Settings
                            });
                        }

                        var canAccessPrompts = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "ChatGpt.Prompts");
                        if (canAccessPrompts)
                        {
                            chatGptMenu.Children.Add(new MenuItem
                            {
                                Title = "Prompts",
                                Href = "/chatgpt/prompts",
                                Icon = Icons.Material.Filled.Description
                            });
                        }

                        var canAccessChat = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "ChatGpt.Chat");
                        if (canAccessChat)
                        {
                            chatGptMenu.Children.Add(new MenuItem
                            {
                                Title = "Chat",
                                Href = "/chatgpt/chat",
                                Icon = Icons.Material.Filled.Forum
                            });
                        }
                    }
                }

                // Only add ChatGPT menu if it has children
                if (chatGptMenu.Children.Any())
                {
                    menuItems.Add(chatGptMenu);
                }
            }

            // Stripe (if enabled, requires tenant)
            if (tenantId.HasValue && await _featureFlagService.IsEnabledAsync("Stripe_Enabled"))
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
        });
    }
}

public class MenuItem
{
    public string Title { get; set; } = string.Empty;
    public string? Href { get; set; }
    public string? Icon { get; set; }
    public List<MenuItem>? Children { get; set; }
}

