namespace Blazor_Bedrock.Data.Models;

public class SavedChart
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Store the entire ChartCreationRequest as JSON
    public string ConfigurationJson { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    
    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
}
