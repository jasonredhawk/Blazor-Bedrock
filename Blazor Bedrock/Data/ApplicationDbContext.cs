using Blazor_Bedrock.Data.Configurations;
using Blazor_Bedrock.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Blazor_Bedrock.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // DbSets
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<UserTenant> UserTenants { get; set; }
    public new DbSet<UserRole> UserRoles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<RolePermission> RolePermissions { get; set; }
    public DbSet<FeatureFlag> FeatureFlags { get; set; }
    public DbSet<MigrationEntry> MigrationEntries { get; set; }
    public DbSet<ChatGptPrompt> ChatGptPrompts { get; set; }
    public DbSet<ChatGptApiKey> ChatGptApiKeys { get; set; }
    public DbSet<ChatGptConversation> ChatGptConversations { get; set; }
    public DbSet<ChatGptMessage> ChatGptMessages { get; set; }
    public DbSet<StripeSubscription> StripeSubscriptions { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<SavedChart> SavedCharts { get; set; }
    public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
    public DbSet<TenantSubscription> TenantSubscriptions { get; set; }
    public DbSet<ChatGptQuestionGroup> ChatGptQuestionGroups { get; set; }
    public DbSet<ChatGptQuestion> ChatGptQuestions { get; set; }
    public DbSet<ChatGptQuestionResponse> ChatGptQuestionResponses { get; set; }
    public DbSet<ApiConfiguration> ApiConfigurations { get; set; }
    public DbSet<RagGroup> RagGroups { get; set; }
    public DbSet<RagGroupDocument> RagGroupDocuments { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply all configurations
        builder.ApplyConfiguration(new ApplicationUserConfiguration());
        builder.ApplyConfiguration(new TenantConfiguration());
        builder.ApplyConfiguration(new UserTenantConfiguration());
        builder.ApplyConfiguration(new ApplicationRoleConfiguration());
        builder.ApplyConfiguration(new UserRoleConfiguration());
        builder.ApplyConfiguration(new PermissionConfiguration());
        builder.ApplyConfiguration(new RolePermissionConfiguration());
        builder.ApplyConfiguration(new FeatureFlagConfiguration());
        builder.ApplyConfiguration(new MigrationEntryConfiguration());
        builder.ApplyConfiguration(new ChatGptPromptConfiguration());
        builder.ApplyConfiguration(new ChatGptApiKeyConfiguration());
        builder.ApplyConfiguration(new ChatGptConversationConfiguration());
        builder.ApplyConfiguration(new ChatGptMessageConfiguration());
        builder.ApplyConfiguration(new StripeSubscriptionConfiguration());
        builder.ApplyConfiguration(new UserProfileConfiguration());
        builder.ApplyConfiguration(new DocumentConfiguration());
        builder.ApplyConfiguration(new SavedChartConfiguration());
        builder.ApplyConfiguration(new SubscriptionPlanConfiguration());
        builder.ApplyConfiguration(new TenantSubscriptionConfiguration());
        builder.ApplyConfiguration(new ChatGptQuestionGroupConfiguration());
        builder.ApplyConfiguration(new ChatGptQuestionConfiguration());
        builder.ApplyConfiguration(new ChatGptQuestionResponseConfiguration());
        builder.ApplyConfiguration(new ApiConfigurationConfiguration());
        builder.ApplyConfiguration(new RagGroupConfiguration());
        builder.ApplyConfiguration(new RagGroupDocumentConfiguration());

        // Rename Identity tables to match our naming convention
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoleClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
    }
}

