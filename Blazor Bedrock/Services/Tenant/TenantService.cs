using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TenantModel = Blazor_Bedrock.Data.Models.Tenant;

namespace Blazor_Bedrock.Services.Tenant;

public interface ITenantService
{
    Task<TenantModel?> GetCurrentTenantAsync();
    Task<List<TenantModel>> GetUserTenantsAsync(string userId);
    Task<bool> SetCurrentTenantAsync(int tenantId);
    Task<bool> UserHasAccessToTenantAsync(string userId, int tenantId);
    int? GetCurrentTenantId();
}

public class TenantService : ITenantService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string TenantIdKey = "CurrentTenantId";

    public TenantService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<TenantModel?> GetCurrentTenantAsync()
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId == null) return null;

        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId);
    }

    public async Task<List<TenantModel>> GetUserTenantsAsync(string userId)
    {
        return await _context.UserTenants
            .Where(ut => ut.UserId == userId && ut.IsActive)
            .Include(ut => ut.Tenant)
            .Select(ut => ut.Tenant)
            .Where(t => t.IsActive)
            .ToListAsync();
    }

    public async Task<bool> SetCurrentTenantAsync(int tenantId)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return false;

        var userId = httpContext.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userId))
        {
            var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return false;
            userId = user.Id;
        }

        // Verify user has access to this tenant
        var hasAccess = await UserHasAccessToTenantAsync(userId, tenantId);
        if (!hasAccess) return false;

        // Store in session/cookie
        httpContext.Session.SetInt32(TenantIdKey, tenantId);
        httpContext.Response.Cookies.Append(TenantIdKey, tenantId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        return true;
    }

    public async Task<bool> UserHasAccessToTenantAsync(string userId, int tenantId)
    {
        return await _context.UserTenants
            .AnyAsync(ut => ut.UserId == userId && ut.TenantId == tenantId && ut.IsActive);
    }

    public int? GetCurrentTenantId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        // Try session first
        var sessionTenantId = httpContext.Session.GetInt32(TenantIdKey);
        if (sessionTenantId.HasValue) return sessionTenantId.Value;

        // Try cookie
        if (httpContext.Request.Cookies.TryGetValue(TenantIdKey, out var cookieValue) &&
            int.TryParse(cookieValue, out var cookieTenantId))
        {
            return cookieTenantId;
        }

        return null;
    }
}

