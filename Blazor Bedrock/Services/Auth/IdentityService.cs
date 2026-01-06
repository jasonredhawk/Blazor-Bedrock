using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;

namespace Blazor_Bedrock.Services.Auth;

public interface IIdentityService
{
    Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password);
    Task<SignInResult> SignInAsync(string email, string password, bool rememberMe);
    Task SignOutAsync();
    Task<ApplicationUser?> GetUserAsync(string userId);
    Task<ExternalLoginInfo?> GetExternalLoginInfoAsync();
    Task<SignInResult> ExternalLoginSignInAsync(string loginProvider, string providerKey, bool isPersistent);
    Task<IdentityResult> CreateExternalUserAsync(ExternalLoginInfo loginInfo);
    Task SignInExternalUserAsync(ApplicationUser user, bool isPersistent);
}

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IDatabaseSyncService _dbSync;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IDatabaseSyncService dbSync)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbSync = dbSync;
    }

    public async Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _userManager.CreateAsync(user, password);
        });
    }

    public async Task<SignInResult> SignInAsync(string email, string password, bool rememberMe)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return SignInResult.Failed;
        }

        return await _dbSync.ExecuteAsync(async () =>
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                // User not found - return failed without revealing this to prevent user enumeration
                return SignInResult.Failed;
            }

            if (!user.IsActive)
            {
                // User exists but is inactive
                return SignInResult.NotAllowed;
            }

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
            
            if (result.Succeeded)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
            }

            return result;
        });
    }

    public async Task SignOutAsync()
    {
        // SignOutAsync doesn't use DbContext, but wrap it for consistency
        await _dbSync.ExecuteAsync(async () =>
        {
            await _signInManager.SignOutAsync();
        });
    }

    public async Task<ApplicationUser?> GetUserAsync(string userId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _userManager.FindByIdAsync(userId);
        });
    }

    public async Task<ExternalLoginInfo?> GetExternalLoginInfoAsync()
    {
        return await _signInManager.GetExternalLoginInfoAsync();
    }

    public async Task<SignInResult> ExternalLoginSignInAsync(string loginProvider, string providerKey, bool isPersistent)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var result = await _signInManager.ExternalLoginSignInAsync(loginProvider, providerKey, isPersistent);
            
            if (result.Succeeded)
            {
                var user = await _userManager.FindByLoginAsync(loginProvider, providerKey);
                if (user != null)
                {
                    user.LastLoginAt = DateTime.UtcNow;
                    await _userManager.UpdateAsync(user);
                }
            }
            
            return result;
        });
    }

    public async Task<IdentityResult> CreateExternalUserAsync(ExternalLoginInfo loginInfo)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            // Get email from claims
            var email = loginInfo.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var name = loginInfo.Principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            
            if (string.IsNullOrEmpty(email))
            {
                return IdentityResult.Failed(new IdentityError { Description = "Email claim not found" });
            }

            // Check if user already exists by email
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                // User exists, add external login to existing account
                var addLoginResult = await _userManager.AddLoginAsync(existingUser, loginInfo);
                if (!addLoginResult.Succeeded)
                {
                    return addLoginResult;
                }
                
                // Sign in the existing user
                await SignInExternalUserAsync(existingUser, isPersistent: true);
                return IdentityResult.Success;
            }

            // Create new user
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true, // External providers verify email
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // Set name if available
            if (!string.IsNullOrEmpty(name))
            {
                var nameParts = name.Split(' ', 2);
                if (nameParts.Length > 0) user.FirstName = nameParts[0];
                if (nameParts.Length > 1) user.LastName = nameParts[1];
            }

            // Create user without password (external auth)
            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return createResult;
            }

            // Add external login
            var addExternalLoginResult = await _userManager.AddLoginAsync(user, loginInfo);
            if (!addExternalLoginResult.Succeeded)
            {
                // Rollback user creation if adding login fails
                await _userManager.DeleteAsync(user);
                return addExternalLoginResult;
            }

            // Sign in the new user
            await SignInExternalUserAsync(user, isPersistent: true);
            return IdentityResult.Success;
        });
    }

    public async Task SignInExternalUserAsync(ApplicationUser user, bool isPersistent)
    {
        await _dbSync.ExecuteAsync(async () =>
        {
            await _signInManager.SignInAsync(user, isPersistent);
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        });
    }
}

