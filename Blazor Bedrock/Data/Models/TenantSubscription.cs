namespace Blazor_Bedrock.Data.Models;

public class TenantSubscription
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int SubscriptionPlanId { get; set; }
    
    // Custom pricing override (if null, use plan's default pricing)
    public decimal? CustomMonthlyPrice { get; set; }
    public decimal? CustomYearlyPrice { get; set; }
    
    // Stripe subscription details
    public string? StripeSubscriptionId { get; set; }
    public string? StripeCustomerId { get; set; }
    public string Status { get; set; } = "active"; // active, canceled, past_due, etc.
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual SubscriptionPlan SubscriptionPlan { get; set; } = null!;
}
