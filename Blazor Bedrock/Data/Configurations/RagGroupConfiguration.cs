using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class RagGroupConfiguration : IEntityTypeConfiguration<RagGroup>
{
    public void Configure(EntityTypeBuilder<RagGroup> builder)
    {
        builder.HasKey(rg => rg.Id);
        builder.Property(rg => rg.Name).IsRequired().HasMaxLength(200);
        builder.Property(rg => rg.Description).HasMaxLength(1000);
        builder.Property(rg => rg.TopK).IsRequired().HasDefaultValue(5);
        builder.Property(rg => rg.PineconeIndexName).HasMaxLength(200);
        
        builder.HasOne(rg => rg.User)
            .WithMany()
            .HasForeignKey(rg => rg.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(rg => rg.Tenant)
            .WithMany()
            .HasForeignKey(rg => rg.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(rg => new { rg.TenantId, rg.UserId });
        builder.HasIndex(rg => rg.PineconeIndexName);
    }
}
