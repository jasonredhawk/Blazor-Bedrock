namespace Blazor_Bedrock.Data.Models;

public class RagGroupDocument
{
    public int Id { get; set; }
    public int RagGroupId { get; set; }
    public int DocumentId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public bool IsIndexed { get; set; } = false; // Track if document is indexed in Pinecone

    // Navigation properties
    public virtual RagGroup RagGroup { get; set; } = null!;
    public virtual Document Document { get; set; } = null!;
}
