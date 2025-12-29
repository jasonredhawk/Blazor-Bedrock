using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.Auth;

public interface IPermissionService
{
    Task<bool> UserHasPermissionAsync(string userId, int tenantId, string permissionName);
    Task<List<Permission>> GetUserPermissionsAsync(string userId, int tenantId);
    Task<List<Permission>> GetAllPermissionsAsync();
    Task<Permission> CreatePermissionAsync(string name, string? description = null, string? category = null);
}

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public PermissionService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<bool> UserHasPermissionAsync(string userId, int tenantId, string permissionName)
    {
        var userRoles = await _context.UserRoles
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        if (!userRoles.Any()) return false;

        return await _context.RolePermissions
            .Where(rp => userRoles.Contains(rp.RoleId))
            .Include(rp => rp.Permission)
            .AnyAsync(rp => rp.Permission.Name.Equals(permissionName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<Permission>> GetUserPermissionsAsync(string userId, int tenantId)
    {
        var userRoles = await _context.UserRoles
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        if (!userRoles.Any()) return new List<Permission>();

        return await _context.RolePermissions
            .Where(rp => userRoles.Contains(rp.RoleId))
            .Include(rp => rp.Permission)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Permission>> GetAllPermissionsAsync()
    {
        return await _context.Permissions.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();
    }

    public async Task<Permission> CreatePermissionAsync(string name, string? description = null, string? category = null)
    {
        var permission = new Permission
        {
            Name = name,
            Description = description,
            Category = category
        };

        _context.Permissions.Add(permission);
        await _context.SaveChangesAsync();
        return permission;
    }
}

