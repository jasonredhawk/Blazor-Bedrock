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
        await SeedPermissionsAsync();
        await SeedAdminRoleAsync();
        await SeedDefaultTenantAsync();
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
            new FeatureFlag { Name = "Users_Enabled", Description = "Enable Users Management", IsEnabled = true },
            new FeatureFlag { Name = "Roles_Enabled", Description = "Enable Roles Management", IsEnabled = true },
            new FeatureFlag { Name = "Migrations_Enabled", Description = "Enable Migration Management", IsEnabled = true },
            new FeatureFlag { Name = "Logger_Enabled", Description = "Enable Application Logger", IsEnabled = true },
            new FeatureFlag { Name = "FeatureFlags_Enabled", Description = "Enable Feature Flags Management", IsEnabled = true },
            new FeatureFlag { Name = "Documents_Enabled", Description = "Enable Document Management", IsEnabled = true }
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

    private async Task SeedPermissionsAsync()
    {
        var permissions = new[]
        {
            // User Management Permissions
            new Permission { Name = "Users.View", Description = "View users list", Category = "User Management" },
            new Permission { Name = "Users.Add", Description = "Add new users", Category = "User Management" },
            new Permission { Name = "Users.Edit", Description = "Edit existing users", Category = "User Management" },
            new Permission { Name = "Users.Delete", Description = "Delete users", Category = "User Management" },
            
            // Role Management Permissions
            new Permission { Name = "Roles.View", Description = "View roles list", Category = "Role Management" },
            new Permission { Name = "Roles.Add", Description = "Add new roles", Category = "Role Management" },
            new Permission { Name = "Roles.Edit", Description = "Edit existing roles", Category = "Role Management" },
            new Permission { Name = "Roles.Delete", Description = "Delete roles", Category = "Role Management" },
            
            // ChatGPT Permissions
            new Permission { Name = "ChatGpt.View", Description = "View ChatGPT menu", Category = "ChatGPT" },
            new Permission { Name = "ChatGpt.Settings", Description = "Access ChatGPT settings", Category = "ChatGPT" },
            new Permission { Name = "ChatGpt.Prompts", Description = "Manage ChatGPT prompts", Category = "ChatGPT" },
            new Permission { Name = "ChatGpt.Chat", Description = "Use ChatGPT chat interface", Category = "ChatGPT" },
            
            // Document Management Permissions
            new Permission { Name = "Documents.View", Description = "View documents list", Category = "Document Management" },
            new Permission { Name = "Documents.Upload", Description = "Upload documents", Category = "Document Management" },
            new Permission { Name = "Documents.Delete", Description = "Delete documents", Category = "Document Management" }
        };

        foreach (var permission in permissions)
        {
            if (!await _context.Permissions.AnyAsync(p => p.Name == permission.Name))
            {
                _context.Permissions.Add(permission);
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task SeedAdminRoleAsync()
    {
        var adminRoleName = "Admin";
        ApplicationRole? adminRole = null;
        
        if (!await _roleManager.RoleExistsAsync(adminRoleName))
        {
            adminRole = new ApplicationRole
            {
                Name = adminRoleName,
                NormalizedName = adminRoleName.ToUpper(),
                Description = "Administrator role with full system access",
                IsSystemRole = true
            };
            await _roleManager.CreateAsync(adminRole);
        }
        else
        {
            adminRole = await _roleManager.FindByNameAsync(adminRoleName);
        }

        // Assign all permissions to Admin role
        if (adminRole != null)
        {
            var allPermissions = await _context.Permissions.ToListAsync();
            foreach (var permission in allPermissions)
            {
                var exists = await _context.RolePermissions
                    .AnyAsync(rp => rp.RoleId == adminRole.Id && rp.PermissionId == permission.Id);
                
                if (!exists)
                {
                    _context.RolePermissions.Add(new RolePermission
                    {
                        RoleId = adminRole.Id,
                        PermissionId = permission.Id
                    });
                }
            }
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedDefaultTenantAsync()
    {
        var defaultTenantName = "Default Organization";
        var defaultTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Name == defaultTenantName);
        
        if (defaultTenant == null)
        {
            defaultTenant = new Tenant
            {
                Name = defaultTenantName,
                Description = "Default organization for system administration",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Tenants.Add(defaultTenant);
            await _context.SaveChangesAsync();
        }
    }

    private async Task SeedAdminUserAsync()
    {
        var adminEmail = "admin@bedrock.local";
        var adminUser = await _userManager.FindByEmailAsync(adminEmail);
        var defaultTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Name == "Default Organization");
        
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
                    // Add to Identity role system
                    await _userManager.AddToRoleAsync(adminUser, adminRole.Name!);
                    
                    // Also assign to default tenant with Admin role (for tenant-scoped permissions)
                    if (defaultTenant != null)
                    {
                        // Add user to tenant
                        var userTenantExists = await _context.UserTenants
                            .AnyAsync(ut => ut.UserId == adminUser.Id && ut.TenantId == defaultTenant.Id);
                        
                        if (!userTenantExists)
                        {
                            _context.UserTenants.Add(new UserTenant
                            {
                                UserId = adminUser.Id,
                                TenantId = defaultTenant.Id,
                                IsActive = true
                            });
                        }
                        
                        // Add Admin role to user for this tenant
                        var userRoleExists = await _context.UserRoles
                            .AnyAsync(ur => ur.UserId == adminUser.Id && ur.RoleId == adminRole.Id && ur.TenantId == defaultTenant.Id);
                        
                        if (!userRoleExists)
                        {
                            _context.UserRoles.Add(new UserRole
                            {
                                UserId = adminUser.Id,
                                RoleId = adminRole.Id,
                                TenantId = defaultTenant.Id
                            });
                        }
                        
                        await _context.SaveChangesAsync();
                    }
                }
            }
        }
        else if (defaultTenant != null)
        {
            // If admin user already exists, ensure they're assigned to default tenant with Admin role
            var adminRole = await _roleManager.FindByNameAsync("Admin");
            if (adminRole != null)
            {
                // Add user to tenant if not already
                var userTenantExists = await _context.UserTenants
                    .AnyAsync(ut => ut.UserId == adminUser.Id && ut.TenantId == defaultTenant.Id);
                
                if (!userTenantExists)
                {
                    _context.UserTenants.Add(new UserTenant
                    {
                        UserId = adminUser.Id,
                        TenantId = defaultTenant.Id,
                        IsActive = true
                    });
                }
                
                // Add Admin role to user for this tenant if not already
                var userRoleExists = await _context.UserRoles
                    .AnyAsync(ur => ur.UserId == adminUser.Id && ur.RoleId == adminRole.Id && ur.TenantId == defaultTenant.Id);
                
                if (!userRoleExists)
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = adminUser.Id,
                        RoleId = adminRole.Id,
                        TenantId = defaultTenant.Id
                    });
                }
                
                await _context.SaveChangesAsync();
            }
        }
    }

    private async Task SeedChatGptPromptsAsync()
    {
        var prompts = new[]
        {
            // General Purpose Assistants
            new ChatGptPrompt
            {
                Name = "General Assistant",
                Description = "A helpful general-purpose assistant for answering questions",
                PromptText = @"You are a helpful and knowledgeable assistant.

Please provide a clear, accurate, and helpful answer to the user's question. If you need to make assumptions or if the question is unclear, please state that in your response.",
                IsSystemPrompt = true
            },
            new ChatGptPrompt
            {
                Name = "Concise Assistant",
                Description = "Provide brief, focused answers",
                PromptText = @"You are a concise assistant that provides brief, focused answers.

Please provide a concise answer (2-4 sentences) that directly addresses the question. Focus on the most relevant information. Be precise and avoid unnecessary details.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Detailed Assistant",
                Description = "Provide comprehensive, detailed explanations",
                PromptText = @"You are a detailed assistant that provides comprehensive explanations.

Please provide a thorough, well-structured response. Include:
- Detailed explanations of concepts
- Relevant examples or analogies when helpful
- Structured format with clear sections
- Use Markdown formatting (headings, bullet points, numbered lists) for better readability

Be thorough and analytical while remaining clear and accessible.",
                IsSystemPrompt = false
            },
            
            // Document Analysis Prompts
            new ChatGptPrompt
            {
                Name = "Document Analysis",
                Description = "Analyze documents and answer questions about them",
                PromptText = @"You are a helpful assistant that analyzes documents. You have access to the following document content:

{documentText}

Please analyze the document carefully and provide accurate, detailed answers based on the content provided. Reference specific sections or passages when relevant. If the document doesn't contain information needed to answer the question, say so clearly.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Document Q&A Assistant",
                Description = "Answer questions about documents with reference to specific content",
                PromptText = @"You are a helpful assistant analyzing documents. Your role is to answer questions about the provided document based on the text content.

Document Content:
{documentText}

Question: {question}

Please provide a detailed, accurate answer based on the document content. Reference specific sections or passages when relevant. If the document doesn't contain information needed to answer the question, say so clearly.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Analytical Summary Assistant",
                Description = "Provide analytical summaries and insights about documents",
                PromptText = @"You are an analytical assistant specializing in document analysis. Your task is to provide insights and summaries based on the document content.

Full Document Text:
{documentText}

Question: {question}

Please provide a comprehensive analytical response. Focus on:
1. Key findings and conclusions relevant to the question
2. Patterns, relationships, or themes that emerge
3. Specific evidence or examples from the document

Structure your response clearly with proper formatting. Use Markdown for better readability (headings, bullet points, bold text for emphasis).",
                IsSystemPrompt = false
            },
            
            // Text Processing Prompts
            new ChatGptPrompt
            {
                Name = "Text Summarization",
                Description = "Summarize long text content",
                PromptText = @"Please provide a concise summary of the following text, highlighting the key points and main ideas.

Text to summarize:
{text}

Format your summary with:
- A brief overview (1-2 sentences)
- Key points in bullet format
- Main conclusions or takeaways",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Bullet Point Summary",
                Description = "A concise point-form analysis of content",
                PromptText = @"Please provide a concise bullet-point summary of the following content. Use Markdown bullet points with clear, structured information.

Content:
{text}

Format your response with:
- **Point 1**: [key point]
- **Point 2**: [key point]
- **Point 3**: [key point]

Be concise and use Markdown-formatted bullet points for clarity.",
                IsSystemPrompt = false
            },
            
            // Specialized Assistants
            new ChatGptPrompt
            {
                Name = "Scientific Advisor",
                Description = "A scientific advisor for questions with a focus on accuracy",
                PromptText = @"You are a scientific advisor providing accurate, evidence-based responses to questions.

Question: {question}

Please provide a well-structured, scientific response. Focus on:
- Accuracy and evidence-based information
- Clear explanations of concepts
- Proper use of scientific terminology
- Structured format (use headings, bullet points, or numbered lists as appropriate)

If the question is outside your area of expertise or requires specific data not provided, please state that clearly.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Technical Expert",
                Description = "A technical expert providing detailed technical explanations",
                PromptText = @"You are a technical expert providing detailed, technical explanations.

Question: {question}

Please provide a comprehensive technical response. Include:
- Technical details and explanations
- Relevant terminology and concepts
- Examples or analogies when helpful
- Structured format with clear sections

Use technical language appropriately, but explain complex concepts clearly. If the question requires domain-specific knowledge that you don't have access to, please indicate that.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Code Assistant",
                Description = "A helpful assistant for programming and code-related questions",
                PromptText = @"You are a helpful programming assistant. You provide clear, well-structured code examples and explanations.

Question: {question}

Please provide:
- Clear, well-commented code examples
- Explanations of how the code works
- Best practices and considerations
- Alternative approaches when relevant

Format code blocks properly and explain your reasoning. If the question is unclear or requires more context, please ask for clarification.",
                IsSystemPrompt = false
            },
            
            // Creative & Style-Based Prompts
            new ChatGptPrompt
            {
                Name = "Funny Assistant",
                Description = "A humorous and entertaining assistant that keeps content accurate but adds wit and humor",
                PromptText = @"You are a hilarious and witty assistant. Your goal is to be both informative AND entertaining - like a stand-up comedian who actually knows their stuff.

Question: {question}

Please provide a funny and entertaining answer. Be witty, use humor, jokes, puns, or amusing analogies while still being accurate and informative. Use Markdown formatting and feel free to use emojis! 🎉

Remember: Be funny, but be accurate! The humor should enhance understanding, not obscure it.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Sarcastic Assistant",
                Description = "A sarcastic and snarky assistant that provides accurate information with a healthy dose of wit and irony",
                PromptText = @"You are a sarcastic and delightfully snarky assistant. You provide accurate answers but with a healthy dose of irony, wit, and gentle sarcasm. Think of yourself as a brilliant but slightly jaded expert who can't help but comment on the obvious.

Question: {question}

Please provide a sarcastically witty answer. Use dry humor, ironic observations, and gentle snark while still being accurate and informative. Use Markdown formatting.

Remember: Be sarcastic, but be helpful. The snark should illuminate, not alienate. 😏",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Child-Friendly Assistant",
                Description = "Simple, clear explanations that even a child can understand - like a \"For Dummies\" handbook",
                PromptText = @"You are a patient and friendly teacher who explains complex topics in the simplest way possible. Imagine you're explaining to a curious 10-year-old who wants to understand everything.

Question: {question}

Please answer in the simplest way possible. Use:
- Simple words (avoid jargon when possible, or explain it if you must use it)
- Real-world analogies or examples (""this is like..."" or ""imagine that..."")
- Short sentences
- Clear, step-by-step explanations
- Markdown formatting with bullet points or numbered lists

Make it so clear that even a child could understand! Simple doesn't mean wrong - be accurate, just explain it like you're talking to your smart 10-year-old cousin!",
                IsSystemPrompt = false
            },
            
            // Business & Professional Prompts
            new ChatGptPrompt
            {
                Name = "Business Advisor",
                Description = "A professional business advisor for business-related questions",
                PromptText = @"You are a professional business advisor providing strategic and practical business insights.

Question: {question}

Please provide a well-structured business response. Focus on:
- Practical, actionable advice
- Strategic considerations
- Risk assessment when relevant
- Clear, professional language
- Structured format with headings and bullet points

If the question requires specific industry knowledge or data not provided, please state that clearly and suggest where such information might be found.",
                IsSystemPrompt = false
            },
            new ChatGptPrompt
            {
                Name = "Writing Assistant",
                Description = "A helpful assistant for writing, editing, and improving text",
                PromptText = @"You are a skilled writing assistant. You help users improve their writing, provide feedback, and suggest edits.

Question: {question}

Please provide:
- Clear writing suggestions and improvements
- Grammar and style corrections when relevant
- Alternative phrasings or word choices
- Structure and organization recommendations
- Examples of improved versions when helpful

Be constructive and specific in your feedback. Use Markdown formatting to clearly show suggestions and improvements.",
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

