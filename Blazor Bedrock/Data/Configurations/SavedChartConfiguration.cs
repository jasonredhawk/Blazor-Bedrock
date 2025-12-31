using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class SavedChartConfiguration : IEntityTypeConfiguration<SavedChart>
{
    public void Configure(EntityTypeBuilder<SavedChart> builder)
    {
        builder.ToTable("SavedCharts");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();
        
        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(255);
        
        builder.Property(x => x.TenantId)
            .IsRequired();
        
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(255);
        
        builder.Property(x => x.Description)
            .HasMaxLength(1000);
        
        builder.Property(x => x.ConfigurationJson)
            .IsRequired()
            .HasColumnType("LONGTEXT");
        
        builder.Property(x => x.CreatedAt)
            .IsRequired();
        
        builder.Property(x => x.UpdatedAt)
            .IsRequired();
        
        builder.Property(x => x.LastUsedAt);
        
        // Relationships
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Indexes
        builder.HasIndex(x => new { x.UserId, x.TenantId });
        builder.HasIndex(x => x.Name);
    }
}
