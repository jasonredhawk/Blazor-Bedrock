namespace Blazor_Bedrock.Data.Models;

public class UserTenant
{
    public string UserId { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
}

