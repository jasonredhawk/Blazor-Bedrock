using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class MigrationEntryConfiguration : IEntityTypeConfiguration<MigrationEntry>
{
    public void Configure(EntityTypeBuilder<MigrationEntry> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.Version).IsRequired().HasMaxLength(50);
        builder.Property(m => m.SqlScript).HasColumnType("LONGTEXT");
        builder.Property(m => m.ErrorMessage).HasMaxLength(2000);
        builder.HasIndex(m => m.Name);
        builder.HasIndex(m => m.Status);
    }
}

