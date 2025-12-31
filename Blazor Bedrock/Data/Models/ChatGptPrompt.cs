namespace Blazor_Bedrock.Data.Models;

public enum PromptType
{
    Chat = 0,      // General chat prompts
    Chart = 1,     // Chart data analysis prompts
    // Future types can be added here
}

public class ChatGptPrompt
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PromptType PromptType { get; set; } = PromptType.Chat;
    public bool IsSystemPrompt { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant? Tenant { get; set; }
}

