namespace Blazor_Bedrock.Data.Models;

public class ChatGptConversation
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int? TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int? PromptId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant? Tenant { get; set; }
    public virtual ICollection<ChatGptMessage> Messages { get; set; } = new List<ChatGptMessage>();
}

public class ChatGptMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string Role { get; set; } = string.Empty; // user, assistant, system
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ChatGptConversation Conversation { get; set; } = null!;
}

