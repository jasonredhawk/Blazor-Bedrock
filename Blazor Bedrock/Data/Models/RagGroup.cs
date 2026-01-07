namespace Blazor_Bedrock.Data.Models;

public class RagGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public int TopK { get; set; } = 5; // Default top-K value for retrieval
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? PineconeIndexName { get; set; } // Store the Pinecone index name for this group

    // Navigation properties
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual ICollection<RagGroupDocument> RagGroupDocuments { get; set; } = new List<RagGroupDocument>();
}
