using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FileName).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Title).HasMaxLength(500);
        builder.Property(d => d.Author).HasMaxLength(200);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(d => d.FileContent).IsRequired();
        builder.Property(d => d.ExtractedText).HasColumnType("LONGTEXT");
        
        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(d => d.Tenant)
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(d => new { d.TenantId, d.UserId });
    }
}

