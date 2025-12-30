namespace Blazor_Bedrock.Data.Models;

public class Document
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public byte[] FileContent { get; set; } = Array.Empty<byte>();
    public string? ExtractedText { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
}

