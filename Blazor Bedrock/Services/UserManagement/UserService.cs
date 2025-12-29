using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Services.UserManagement;

public interface IUserService
{
    Task<List<ApplicationUser>> GetUsersAsync(int tenantId);
    Task<ApplicationUser?> GetUserAsync(string userId);
    Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password, int tenantId);
    Task<IdentityResult> UpdateUserAsync(ApplicationUser user);
    Task<IdentityResult> DeleteUserAsync(string userId);
    Task<bool> AssignUserToTenantAsync(string userId, int tenantId);
    Task<bool> RemoveUserFromTenantAsync(string userId, int tenantId);
    Task<List<ApplicationRole>> GetUserRolesAsync(string userId, int tenantId);
    Task<bool> AssignRoleToUserAsync(string userId, string roleId, int tenantId);
    Task<bool> RemoveRoleFromUserAsync(string userId, string roleId, int tenantId);
}

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public UserService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<List<ApplicationUser>> GetUsersAsync(int tenantId)
    {
        return await _context.UserTenants
            .Where(ut => ut.TenantId == tenantId && ut.IsActive)
            .Include(ut => ut.User)
            .Select(ut => ut.User)
            .Where(u => u.IsActive)
            .ToListAsync();
    }

    public async Task<ApplicationUser?> GetUserAsync(string userId)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    public async Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password, int tenantId)
    {
        var result = await _userManager.CreateAsync(user, password);
        
        if (result.Succeeded)
        {
            var userTenant = new UserTenant
            {
                UserId = user.Id,
                TenantId = tenantId,
                IsActive = true
            };
            _context.UserTenants.Add(userTenant);
            await _context.SaveChangesAsync();
        }

        return result;
    }

    public async Task<IdentityResult> UpdateUserAsync(ApplicationUser user)
    {
        return await _userManager.UpdateAsync(user);
    }

    public async Task<IdentityResult> DeleteUserAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return IdentityResult.Failed();

        user.IsActive = false;
        return await _userManager.UpdateAsync(user);
    }

    public async Task<bool> AssignUserToTenantAsync(string userId, int tenantId)
    {
        var exists = await _context.UserTenants
            .AnyAsync(ut => ut.UserId == userId && ut.TenantId == tenantId);

        if (!exists)
        {
            var userTenant = new UserTenant
            {
                UserId = userId,
                TenantId = tenantId,
                IsActive = true
            };
            _context.UserTenants.Add(userTenant);
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> RemoveUserFromTenantAsync(string userId, int tenantId)
    {
        var userTenant = await _context.UserTenants
            .FirstOrDefaultAsync(ut => ut.UserId == userId && ut.TenantId == tenantId);

        if (userTenant != null)
        {
            userTenant.IsActive = false;
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<List<ApplicationRole>> GetUserRolesAsync(string userId, int tenantId)
    {
        return await _context.UserRoles
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Include(ur => ur.Role)
            .Select(ur => ur.Role)
            .ToListAsync();
    }

    public async Task<bool> AssignRoleToUserAsync(string userId, string roleId, int tenantId)
    {
        var exists = await _context.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId && ur.TenantId == tenantId);

        if (!exists)
        {
            var userRole = new UserRole
            {
                UserId = userId,
                RoleId = roleId,
                TenantId = tenantId
            };
            _context.UserRoles.Add(userRole);
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> RemoveRoleFromUserAsync(string userId, string roleId, int tenantId)
    {
        var userRole = await _context.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId && ur.TenantId == tenantId);

        if (userRole != null)
        {
            _context.UserRoles.Remove(userRole);
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }
}

