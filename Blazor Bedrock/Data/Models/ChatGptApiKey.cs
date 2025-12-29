namespace Blazor_Bedrock.Data.Models;

public class ChatGptApiKey
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int? TenantId { get; set; }
    public string EncryptedApiKey { get; set; } = string.Empty;
    public string? PreferredModel { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant? Tenant { get; set; }
}

