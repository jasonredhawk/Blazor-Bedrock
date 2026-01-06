using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ApiConfigurationConfiguration : IEntityTypeConfiguration<ApiConfiguration>
{
    public void Configure(EntityTypeBuilder<ApiConfiguration> builder)
    {
        builder.HasKey(ac => ac.Id);
        builder.Property(ac => ac.ServiceName).IsRequired().HasMaxLength(100);
        builder.Property(ac => ac.EncryptedConfiguration).IsRequired().HasMaxLength(5000);
        builder.Property(ac => ac.CreatedByUserId).HasMaxLength(450);
        builder.Property(ac => ac.UpdatedByUserId).HasMaxLength(450);
        builder.HasIndex(ac => ac.ServiceName).IsUnique();
    }
}
