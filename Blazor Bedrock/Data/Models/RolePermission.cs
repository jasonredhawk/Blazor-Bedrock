namespace Blazor_Bedrock.Data.Models;

public class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public int PermissionId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ApplicationRole Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}

