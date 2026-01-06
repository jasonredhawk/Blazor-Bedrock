namespace Blazor_Bedrock.Data.Models;

public class ApiConfiguration
{
    public int Id { get; set; }
    public string ServiceName { get; set; } = string.Empty; // e.g., "ChatGPT", "GoogleAuth", "FacebookAuth"
    public string EncryptedConfiguration { get; set; } = string.Empty; // JSON encrypted string containing key-value pairs
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
}
