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
            var tenantId = tenantService.GetCurrentTenantId();
            
            // If no tenant selected, try to get user's first tenant
            if (tenantId == null)
            {
                var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.GetUserAsync(context.User);
                
                if (user != null)
                {
                    var userTenants = await tenantService.GetUserTenantsAsync(user.Id);
                    var firstTenant = userTenants.FirstOrDefault();
                    if (firstTenant != null)
                    {
                        await tenantService.SetCurrentTenantAsync(firstTenant.Id);
                    }
                }
            }
        }

        await _next(context);
    }
}

