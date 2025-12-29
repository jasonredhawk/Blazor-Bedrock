using Microsoft.AspNetCore.Identity;

namespace Blazor_Bedrock.Data.Models;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public virtual ICollection<UserTenant> UserTenants { get; set; } = new List<UserTenant>();
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual UserProfile? UserProfile { get; set; }
    public virtual ICollection<ChatGptApiKey> ChatGptApiKeys { get; set; } = new List<ChatGptApiKey>();
    public virtual ICollection<ChatGptConversation> ChatGptConversations { get; set; } = new List<ChatGptConversation>();
}

