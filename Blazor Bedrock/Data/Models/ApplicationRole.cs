using Microsoft.AspNetCore.Identity;

namespace Blazor_Bedrock.Data.Models;

public class ApplicationRole : IdentityRole
{
    public int? TenantId { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Tenant? Tenant { get; set; }
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

