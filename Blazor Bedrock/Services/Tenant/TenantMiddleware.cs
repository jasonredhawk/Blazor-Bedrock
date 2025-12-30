using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Identity;

namespace Blazor_Bedrock.Services.Tenant;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantService tenantService)
    {
        // Skip tenant resolution for auth pages and static files
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/auth/") || 
            path.StartsWith("/_") || 
            path.StartsWith("/health") ||
            path.StartsWith("/css/") ||
            path.StartsWith("/js/") ||
            path.StartsWith("/lib/"))
        {
            await _next(context);
            return;
        }

        // If user is authenticated, ensure they have a tenant selected
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.GetUserAsync(context.User);
            
            if (user != null)
            {
                var tenantId = tenantService.GetCurrentTenantId();
                
                // If no tenant selected, try to restore from user's last selected tenant or first tenant
                if (tenantId == null)
                {
                    var userTenants = await tenantService.GetUserTenantsAsync(user.Id);
                    
                    // Try to restore from user's last selected tenant (if they still have access)
                    int? tenantToSelect = null;
                    var lastSelectedTenantId = await tenantService.GetLastSelectedTenantIdAsync(user.Id);
                    if (lastSelectedTenantId.HasValue)
                    {
                        var hasAccess = await tenantService.UserHasAccessToTenantAsync(user.Id, lastSelectedTenantId.Value);
                        if (hasAccess && userTenants.Any(t => t.Id == lastSelectedTenantId.Value))
                        {
                            tenantToSelect = lastSelectedTenantId.Value;
                        }
                    }
                    
                    // If no valid last selected tenant, use first tenant
                    if (!tenantToSelect.HasValue)
                    {
                        tenantToSelect = userTenants.FirstOrDefault()?.Id;
                    }
                    
                    if (tenantToSelect.HasValue)
                    {
                        await tenantService.SetCurrentTenantAsync(tenantToSelect.Value);
                    }
                }
                else
                {
                    // Verify the tenant from cookie/session is still valid
                    var hasAccess = await tenantService.UserHasAccessToTenantAsync(user.Id, tenantId.Value);
                    if (!hasAccess)
                    {
                        // User no longer has access to this tenant, clear it and select first available
                        var userTenants = await tenantService.GetUserTenantsAsync(user.Id);
                        var firstTenant = userTenants.FirstOrDefault();
                        if (firstTenant != null)
                        {
                            await tenantService.SetCurrentTenantAsync(firstTenant.Id);
                        }
                        else
                        {
                            // Clear invalid tenant selection
                            try
                            {
                                context.Session.Remove("CurrentTenantId");
                                context.Response.Cookies.Delete("CurrentTenantId");
                            }
                            catch
                            {
                                // Ignore errors when clearing
                            }
                        }
                    }
                }
            }
        }

        await _next(context);
    }
}

