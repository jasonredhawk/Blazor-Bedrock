namespace Blazor_Bedrock.Data.Models;

public class UserRole
{
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual ApplicationRole Role { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
}

