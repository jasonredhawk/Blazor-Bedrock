namespace Blazor_Bedrock.Data.Models;

public class ChatGptPrompt
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemPrompt { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant? Tenant { get; set; }
}

