using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.RoleManagement;

public interface IRoleService
{
    Task<List<ApplicationRole>> GetRolesAsync(int? tenantId);
    Task<ApplicationRole?> GetRoleAsync(string roleId);
    Task<IdentityResult> CreateRoleAsync(ApplicationRole role);
    Task<IdentityResult> UpdateRoleAsync(ApplicationRole role);
    Task<IdentityResult> DeleteRoleAsync(string roleId);
    Task<List<Permission>> GetRolePermissionsAsync(string roleId);
    Task<bool> AssignPermissionToRoleAsync(string roleId, int permissionId);
    Task<bool> RemovePermissionFromRoleAsync(string roleId, int permissionId);
}

public class RoleService : IRoleService
{
    private readonly ApplicationDbContext _context;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IDatabaseSyncService _dbSync;

    public RoleService(ApplicationDbContext context, RoleManager<ApplicationRole> roleManager, IDatabaseSyncService dbSync)
    {
        _context = context;
        _roleManager = roleManager;
        _dbSync = dbSync;
    }

    public async Task<List<ApplicationRole>> GetRolesAsync(int? tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var query = _context.Roles.AsQueryable();
            
            if (tenantId.HasValue)
            {
                query = query.Where(r => r.TenantId == tenantId || r.TenantId == null);
            }

            return await query.ToListAsync();
        });
    }

    public async Task<ApplicationRole?> GetRoleAsync(string roleId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _roleManager.FindByIdAsync(roleId);
        });
    }

    public async Task<IdentityResult> CreateRoleAsync(ApplicationRole role)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _roleManager.CreateAsync(role);
        });
    }

    public async Task<IdentityResult> UpdateRoleAsync(ApplicationRole role)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _roleManager.UpdateAsync(role);
        });
    }

    public async Task<IdentityResult> DeleteRoleAsync(string roleId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null || role.IsSystemRole) 
                return IdentityResult.Failed(new IdentityError { Description = "Cannot delete system role" });

            return await _roleManager.DeleteAsync(role);
        });
    }

    public async Task<List<Permission>> GetRolePermissionsAsync(string roleId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .Include(rp => rp.Permission)
                .Select(rp => rp.Permission)
                .ToListAsync();
        });
    }

    public async Task<bool> AssignPermissionToRoleAsync(string roleId, int permissionId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var exists = await _context.RolePermissions
                .AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (!exists)
            {
                var rolePermission = new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId
                };
                _context.RolePermissions.Add(rolePermission);
                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        });
    }

    public async Task<bool> RemovePermissionFromRoleAsync(string roleId, int permissionId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var rolePermission = await _context.RolePermissions
                .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (rolePermission != null)
            {
                _context.RolePermissions.Remove(rolePermission);
                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        });
    }
}

