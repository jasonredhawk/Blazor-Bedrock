namespace Blazor_Bedrock.Data.Models;

public class ChatGptQuestionGroup
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant? Tenant { get; set; }
    public virtual ICollection<ChatGptQuestion> Questions { get; set; } = new List<ChatGptQuestion>();
}

public class ChatGptQuestion
{
    public int Id { get; set; }
    public int? TenantId { get; set; }
    public int? GroupId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Order { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant? Tenant { get; set; }
    public virtual ChatGptQuestionGroup? Group { get; set; }
    public virtual ICollection<ChatGptQuestionResponse> Responses { get; set; } = new List<ChatGptQuestionResponse>();
}

public class ChatGptQuestionResponse
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public int? QuestionId { get; set; } // Nullable to allow manual questions
    public int? DocumentId { get; set; }
    public int? PromptId { get; set; }
    public string Response { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ChatGptConversation Conversation { get; set; } = null!;
    public virtual ChatGptQuestion? Question { get; set; } // Nullable to allow manual questions
    public virtual Document? Document { get; set; }
    public virtual ChatGptPrompt? Prompt { get; set; }
}
