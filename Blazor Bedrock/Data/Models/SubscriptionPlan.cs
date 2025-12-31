namespace Blazor_Bedrock.Data.Models;

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // Free, Basic, Professional, Enterprise
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Pricing
    public decimal? MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public string? StripeMonthlyPriceId { get; set; }
    public string? StripeYearlyPriceId { get; set; }
    public string Currency { get; set; } = "USD";
    
    // Limits
    public int? MaxDocuments { get; set; } // null = unlimited
    public int? MaxConversations { get; set; } // null = unlimited
    public int? MaxQueriesPerConversation { get; set; } // null = unlimited
    public int? MaxUsers { get; set; } // null = unlimited
    public int? MaxOrganizations { get; set; } // null = unlimited (only relevant for MasterAdmin)
    public int? MaxCharts { get; set; } // null = unlimited
    public bool CanUseChatGptAnalysis { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
    public virtual ICollection<TenantSubscription> TenantSubscriptions { get; set; } = new List<TenantSubscription>();
}
