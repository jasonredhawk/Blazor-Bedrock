namespace Blazor_Bedrock.Data.Models;

public class Tenant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Subscription
    public int? SubscriptionPlanId { get; set; }
    
    // ChatGPT Configuration
    public string? PreferredModel { get; set; }
    
    // Navigation properties
    public virtual SubscriptionPlan? SubscriptionPlan { get; set; }
    public virtual ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<ApplicationRole> Roles { get; set; } = new List<ApplicationRole>();
    public virtual ICollection<ChatGptPrompt> ChatGptPrompts { get; set; } = new List<ChatGptPrompt>();
    public virtual ICollection<StripeSubscription> StripeSubscriptions { get; set; } = new List<StripeSubscription>();
    public virtual TenantSubscription? CurrentSubscription { get; set; }
}

