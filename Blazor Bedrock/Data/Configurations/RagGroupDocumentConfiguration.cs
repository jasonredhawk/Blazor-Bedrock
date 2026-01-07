using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class RagGroupDocumentConfiguration : IEntityTypeConfiguration<RagGroupDocument>
{
    public void Configure(EntityTypeBuilder<RagGroupDocument> builder)
    {
        builder.HasKey(rgd => rgd.Id);
        
        builder.HasOne(rgd => rgd.RagGroup)
            .WithMany(rg => rg.RagGroupDocuments)
            .HasForeignKey(rgd => rgd.RagGroupId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(rgd => rgd.Document)
            .WithMany()
            .HasForeignKey(rgd => rgd.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
            
        // Ensure a document can only be added once to a group
        builder.HasIndex(rgd => new { rgd.RagGroupId, rgd.DocumentId }).IsUnique();
    }
}
