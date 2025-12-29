using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Identity;

namespace Blazor_Bedrock.Services.Auth;

public interface IIdentityService
{
    Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password);
    Task<SignInResult> SignInAsync(string email, string password, bool rememberMe);
    Task SignOutAsync();
    Task<ApplicationUser?> GetUserAsync(string userId);
}

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<IdentityResult> CreateUserAsync(ApplicationUser user, string password)
    {
        return await _userManager.CreateAsync(user, password);
    }

    public async Task<SignInResult> SignInAsync(string email, string password, bool rememberMe)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return SignInResult.Failed;
        }

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
    }

    public async Task SignOutAsync()
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<ApplicationUser?> GetUserAsync(string userId)
    {
        return await _userManager.FindByIdAsync(userId);
    }
}

