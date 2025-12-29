using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Data;

public class DatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public DatabaseSeeder(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task SeedAsync()
    {
        await SeedFeatureFlagsAsync();
        await SeedAdminRoleAsync();
        await SeedAdminUserAsync();
        await SeedChatGptPromptsAsync();
    }

    private async Task SeedFeatureFlagsAsync()
    {
        var flags = new[]
        {
            new FeatureFlag { Name = "Auth_Google", Description = "Enable Google Authentication", IsEnabled = false },
            new FeatureFlag { Name = "Auth_Facebook", Description = "Enable Facebook Authentication", IsEnabled = false },
            new FeatureFlag { Name = "ChatGpt_Enabled", Description = "Enable ChatGPT Integration", IsEnabled = true },
            new FeatureFlag { Name = "Stripe_Enabled", Description = "Enable Stripe Payments", IsEnabled = false },
            new FeatureFlag { Name = "Migrations_Enabled", Description = "Enable Migration Management", IsEnabled = true },
            new FeatureFlag { Name = "Logger_Enabled", Description = "Enable Application Logger", IsEnabled = true }
        };

        foreach (var flag in flags)
        {
            if (!await _context.FeatureFlags.AnyAsync(f => f.Name == flag.Name))
            {
                _context.FeatureFlags.Add(flag);
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task SeedAdminRoleAsync()
    {
        var adminRoleName = "Admin";
        if (!await _roleManager.RoleExistsAsync(adminRoleName))
        {
            var adminRole = new ApplicationRole
            {
                Name = adminRoleName,
                NormalizedName = adminRoleName.ToUpper(),
                Description = "Administrator role with full system access",
                IsSystemRole = true
            };
            await _roleManager.CreateAsync(adminRole);
        }
    }

    private async Task SeedAdminUserAsync()
    {
        var adminEmail = "admin@bedrock.local";
        var adminUser = await _userManager.FindByEmailAsync(adminEmail);
        
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "System",
                LastName = "Administrator",
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(adminUser, "Admin@123!");
            if (result.Succeeded)
            {
                var adminRole = await _roleManager.FindByNameAsync("Admin");
                if (adminRole != null)
                {
                    await _userManager.AddToRoleAsync(adminUser, adminRole.Name!);
                }
            }
        }
    }

    private async Task SeedChatGptPromptsAsync()
    {
        var prompts = new[]
        {
            new ChatGptPrompt
            {
                Name = "Document Analysis",
                Description = "Analyze a document and answer questions about it",
                PromptText = "You are a helpful assistant that analyzes documents. The user will provide you with document content and ask questions about it. Please analyze the document carefully and provide accurate, detailed answers based on the content provided.",
                IsSystemPrompt = true
            },
            new ChatGptPrompt
            {
                Name = "Text Summarization",
                Description = "Summarize long text content",
                PromptText = "Please provide a concise summary of the following text, highlighting the key points and main ideas.",
                IsSystemPrompt = false
            }
        };

        foreach (var prompt in prompts)
        {
            if (!await _context.ChatGptPrompts.AnyAsync(p => p.Name == prompt.Name && p.TenantId == null))
            {
                _context.ChatGptPrompts.Add(prompt);
            }
        }

        await _context.SaveChangesAsync();
    }
}

