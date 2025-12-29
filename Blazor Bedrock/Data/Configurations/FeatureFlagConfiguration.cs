using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.HasKey(ff => ff.Id);
        builder.Property(ff => ff.Name).IsRequired().HasMaxLength(100);
        builder.Property(ff => ff.Description).HasMaxLength(500);
        builder.HasIndex(ff => ff.Name).IsUnique();
    }
}

