using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services.FeatureFlag;
using Blazor_Bedrock.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
                Icon = "bi bi-house"
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
                    Icon = "bi bi-gear",
                    Children = new List<MenuItem>()
                };

                // If Admin, show menu items based on feature flags
                if (isAdmin)
                {
                    // Users (if feature flag enabled)
                    if (await _featureFlagService.IsEnabledAsync("Users_Enabled"))
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Users",
                            Href = "/admin/users",
                            Icon = "bi bi-people"
                        });
                    }

                    // Roles (if feature flag enabled)
                    if (await _featureFlagService.IsEnabledAsync("Roles_Enabled"))
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Roles",
                            Href = "/admin/roles",
                            Icon = "bi bi-shield-check"
                        });
                    }

                    // Feature Settings (if enabled)
                    if (await _featureFlagService.IsEnabledAsync("FeatureFlags_Enabled"))
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Feature Settings",
                            Href = "/admin/feature-settings",
                            Icon = "bi bi-toggle-on"
                        });
                    }
                }
                else
                {
                    // Non-admin users: check permissions and feature flags
                    var canViewUsers = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "Users.View");
                    if (canViewUsers && await _featureFlagService.IsEnabledAsync("Users_Enabled"))
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Users",
                            Href = "/admin/users",
                            Icon = "bi bi-people"
                        });
                    }

                    var canViewRoles = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "Roles.View");
                    if (canViewRoles && await _featureFlagService.IsEnabledAsync("Roles_Enabled"))
                    {
                        settingsMenu.Children.Add(new MenuItem
                        {
                            Title = "Roles",
                            Href = "/admin/roles",
                            Icon = "bi bi-shield-check"
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
                    Icon = "bi bi-chat-dots",
                    Children = new List<MenuItem>()
                };

                // If Admin, show all ChatGPT menu items
                if (isAdmin)
                {
                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Settings",
                        Href = "/chatgpt/settings",
                        Icon = "bi bi-gear"
                    });

                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Prompts",
                        Href = "/chatgpt/prompts",
                        Icon = "bi bi-file-text"
                    });

                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Chat",
                        Href = "/chatgpt/chat",
                        Icon = "bi bi-chat-dots"
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
                                Icon = "bi bi-gear"
                            });
                        }

                        var canAccessPrompts = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "ChatGpt.Prompts");
                        if (canAccessPrompts)
                        {
                            chatGptMenu.Children.Add(new MenuItem
                            {
                                Title = "Prompts",
                                Href = "/chatgpt/prompts",
                                Icon = "bi bi-file-text"
                            });
                        }

                        var canAccessChat = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "ChatGpt.Chat");
                        if (canAccessChat)
                        {
                            chatGptMenu.Children.Add(new MenuItem
                            {
                                Title = "Chat",
                                Href = "/chatgpt/chat",
                                Icon = "bi bi-chat-dots"
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

            // Documents Menu (Permission-based, requires tenant)
            if (tenantId.HasValue && await _featureFlagService.IsEnabledAsync("Documents_Enabled"))
            {
                var documentsMenu = new MenuItem
                {
                    Title = "Documents",
                    Icon = "bi bi-file-earmark",
                    Children = new List<MenuItem>()
                };

                // If Admin, show all Documents menu items
                if (isAdmin)
                {
                    documentsMenu.Children.Add(new MenuItem
                    {
                        Title = "Browse",
                        Href = "/documents",
                        Icon = "bi bi-folder"
                    });
                    documentsMenu.Children.Add(new MenuItem
                    {
                        Title = "Upload",
                        Href = "/documents/upload",
                        Icon = "bi bi-upload"
                    });
                }
                else
                {
                    // Non-admin users: check permissions
                    var canViewDocuments = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "Documents.View");
                    if (canViewDocuments)
                    {
                        documentsMenu.Children.Add(new MenuItem
                        {
                            Title = "Browse",
                            Href = "/documents",
                            Icon = "bi bi-folder"
                        });
                    }

                    var canUploadDocuments = await _permissionService.UserHasPermissionAsync(userId, tenantId.Value, "Documents.Upload");
                    if (canUploadDocuments)
                    {
                        documentsMenu.Children.Add(new MenuItem
                        {
                            Title = "Upload",
                            Href = "/documents/upload",
                            Icon = "bi bi-upload"
                        });
                    }
                }

                // Only add Documents menu if it has children
                if (documentsMenu.Children.Any())
                {
                    menuItems.Add(documentsMenu);
                }
            }

            // Stripe (if enabled, requires tenant)
            if (tenantId.HasValue && await _featureFlagService.IsEnabledAsync("Stripe_Enabled"))
            {
                menuItems.Add(new MenuItem
                {
                    Title = "Payments",
                    Href = "/stripe/subscriptions",
                    Icon = "bi bi-credit-card"
                });
            }

            // SuperAdmin - Migrations
            if (isAdmin && await _featureFlagService.IsEnabledAsync("Migrations_Enabled"))
            {
                menuItems.Add(new MenuItem
                {
                    Title = "Migrations",
                    Href = "/superadmin/migrations",
                    Icon = "bi bi-database"
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

