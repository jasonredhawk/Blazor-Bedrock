namespace Blazor_Bedrock.Data.Models;

public class StripeSubscription
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string StripeSubscriptionId { get; set; } = string.Empty;
    public string StripeCustomerId { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public string Status { get; set; } = string.Empty; // active, canceled, past_due, etc.
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
}

