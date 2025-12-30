using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
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
    private readonly IDatabaseSyncService _dbSync;

    public PermissionService(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IDatabaseSyncService dbSync)
    {
        _context = context;
        _userManager = userManager;
        _dbSync = dbSync;
    }

    public async Task<bool> UserHasPermissionAsync(string userId, int tenantId, string permissionName)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            // Check if user has Admin role for this tenant - Admin has all permissions
            var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole != null)
            {
                var isAdmin = await _context.UserRoles
                    .AnyAsync(ur => ur.UserId == userId && ur.RoleId == adminRole.Id && ur.TenantId == tenantId);
                
                if (isAdmin)
                {
                    return true; // Admin has all permissions
                }
            }

            // Check global Admin role as fallback
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var isGlobalAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                if (isGlobalAdmin)
                {
                    return true; // Global Admin has all permissions
                }
            }

            // Check specific permissions for non-admin users
            var userRoles = await _context.UserRoles
                .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            if (!userRoles.Any()) return false;

            return await _context.RolePermissions
                .Where(rp => userRoles.Contains(rp.RoleId))
                .Include(rp => rp.Permission)
                .AnyAsync(rp => rp.Permission.Name.Equals(permissionName, StringComparison.OrdinalIgnoreCase));
        });
    }

    public async Task<List<Permission>> GetUserPermissionsAsync(string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
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
        });
    }

    public async Task<List<Permission>> GetAllPermissionsAsync()
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.Permissions.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();
        });
    }

    public async Task<Permission> CreatePermissionAsync(string name, string? description = null, string? category = null)
    {
        return await _dbSync.ExecuteAsync(async () =>
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
        });
    }
}

