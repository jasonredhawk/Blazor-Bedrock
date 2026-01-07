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
            // Clear feature flag cache inside the lock to ensure we get fresh data
            // This ensures menu reflects current feature flag state
            _featureFlagService.ClearCache();
            var menuItems = new List<MenuItem>();
            
            // Check if user is MasterAdmin
            var user = await _userManager.FindByIdAsync(userId);
            var isMasterAdmin = user?.IsMasterAdmin ?? false;
            
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
            if (!isAdmin && user != null)
            {
                isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
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

            // Documents Menu (Feature flag-based, requires tenant)
            var documentsEnabled = await _featureFlagService.IsEnabledAsync("Documents_Enabled");
            var ragEnabled = await _featureFlagService.IsEnabledAsync("RAG_Enabled");
            if (tenantId.HasValue && documentsEnabled)
            {
                var documentsMenu = new MenuItem
                {
                    Title = "Documents",
                    Icon = "bi bi-file-earmark",
                    Children = new List<MenuItem>()
                };

                // Show menu items if Admin
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
                    if (ragEnabled)
                    {
                        documentsMenu.Children.Add(new MenuItem
                        {
                            Title = "Knowledge Base",
                            Href = "/documents/knowledge-base",
                            Icon = "bi bi-book"
                        });
                    }
                }
                else
                {
                    // For non-admin: show menu items if feature is enabled (pages will handle access control)
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
                    if (ragEnabled)
                    {
                        documentsMenu.Children.Add(new MenuItem
                        {
                            Title = "Knowledge Base",
                            Href = "/documents/knowledge-base",
                            Icon = "bi bi-book"
                        });
                    }
                }

                menuItems.Add(documentsMenu);
            }

            // Charts Menu (Feature flag-based, requires tenant)
            var chartsEnabled = await _featureFlagService.IsEnabledAsync("Charts_Enabled");
            if (tenantId.HasValue && chartsEnabled)
            {
                var chartsMenu = new MenuItem
                {
                    Title = "Charts",
                    Icon = "bi bi-bar-chart",
                    Children = new List<MenuItem>()
                };

                // Show menu items if Admin
                if (isAdmin)
                {
                    chartsMenu.Children.Add(new MenuItem
                    {
                        Title = "Browse",
                        Href = "/charts",
                        Icon = "bi bi-folder"
                    });
                    chartsMenu.Children.Add(new MenuItem
                    {
                        Title = "Create",
                        Href = "/charts/create",
                        Icon = "bi bi-plus-circle"
                    });
                }
                else
                {
                    // For non-admin: show menu items if feature is enabled (pages will handle access control)
                    chartsMenu.Children.Add(new MenuItem
                    {
                        Title = "Browse",
                        Href = "/charts",
                        Icon = "bi bi-folder"
                    });
                    chartsMenu.Children.Add(new MenuItem
                    {
                        Title = "Create",
                        Href = "/charts/create",
                        Icon = "bi bi-plus-circle"
                    });
                }

                menuItems.Add(chartsMenu);
            }

            // ChatGPT Menu (Feature flag-based, requires tenant)
            var chatGptEnabled = await _featureFlagService.IsEnabledAsync("ChatGpt_Enabled");
            if (tenantId.HasValue && chatGptEnabled)
            {
                var chatGptMenu = new MenuItem
                {
                    Title = "ChatGPT",
                    Icon = "bi bi-chat-dots",
                    Children = new List<MenuItem>()
                };

                // Show menu items if Admin
                if (isAdmin)
                {
                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Prompts",
                        Href = "/chatgpt/prompts",
                        Icon = "bi bi-file-text"
                    });

                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Questions",
                        Href = "/chatgpt/questions",
                        Icon = "bi bi-question-circle"
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
                    // For non-admin: show menu items if feature is enabled (pages will handle access control)
                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Prompts",
                        Href = "/chatgpt/prompts",
                        Icon = "bi bi-file-text"
                    });

                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Questions",
                        Href = "/chatgpt/questions",
                        Icon = "bi bi-question-circle"
                    });

                    chatGptMenu.Children.Add(new MenuItem
                    {
                        Title = "Chat",
                        Href = "/chatgpt/chat",
                        Icon = "bi bi-chat-dots"
                    });
                }

                menuItems.Add(chatGptMenu);
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

                // Show menu items based on feature flags (pages will handle access control)
                var usersEnabled = await _featureFlagService.IsEnabledAsync("Users_Enabled");
                var rolesEnabled = await _featureFlagService.IsEnabledAsync("Roles_Enabled");
                
                // Show menu items if feature flags are enabled
                if (usersEnabled)
                {
                    settingsMenu.Children.Add(new MenuItem
                    {
                        Title = "Users",
                        Href = "/admin/users",
                        Icon = "bi bi-people"
                    });
                }

                if (rolesEnabled)
                {
                    settingsMenu.Children.Add(new MenuItem
                    {
                        Title = "Roles",
                        Href = "/admin/roles",
                        Icon = "bi bi-shield-check"
                    });
                }

                // Add Settings menu if it has children
                if (settingsMenu.Children.Any())
                {
                    menuItems.Add(settingsMenu);
                }
            }

            // Subscription (if enabled, requires tenant) - for all users
            if (tenantId.HasValue && await _featureFlagService.IsEnabledAsync("Subscriptions"))
            {
                menuItems.Add(new MenuItem
                {
                    Title = "Subscription",
                    Href = "/subscription",
                    Icon = "bi bi-credit-card"
                });
            }

            // MasterAdmin Menu (only for MasterAdmin users)
            if (isMasterAdmin)
            {
                var masterAdminMenu = new MenuItem
                {
                    Title = "MasterAdmin",
                    Icon = "bi bi-shield-fill",
                    Children = new List<MenuItem>()
                };

                // Subscription submenu items (only show if Subscriptions feature is enabled)
                var subscriptionsEnabled = await _featureFlagService.IsEnabledAsync("Subscriptions");
                if (subscriptionsEnabled)
                {
                    // Subscription Models
                    masterAdminMenu.Children.Add(new MenuItem
                    {
                        Title = "Subscription Models",
                        Href = "/masteradmin/subscription-models",
                        Icon = "bi bi-credit-card-2-front"
                    });

                    // Organization Subscriptions
                    masterAdminMenu.Children.Add(new MenuItem
                    {
                        Title = "Organization Subscriptions",
                        Href = "/masteradmin/organization-subscriptions",
                        Icon = "bi bi-building"
                    });
                }

                // Features Configuration (API keys and settings)
                masterAdminMenu.Children.Add(new MenuItem
                {
                    Title = "Features",
                    Href = "/masteradmin/features",
                    Icon = "bi bi-sliders"
                });

                // Migrations (moved from root)
                if (await _featureFlagService.IsEnabledAsync("Migrations_Enabled"))
                {
                    masterAdminMenu.Children.Add(new MenuItem
                    {
                        Title = "Migrations",
                        Href = "/superadmin/migrations",
                        Icon = "bi bi-database"
                    });
                }

                menuItems.Add(masterAdminMenu);
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

