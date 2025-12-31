using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Domain).HasMaxLength(255);
        builder.HasIndex(t => t.Name);
        builder.HasIndex(t => t.Domain);
        
        builder.HasOne(t => t.SubscriptionPlan)
            .WithMany(sp => sp.Tenants)
            .HasForeignKey(t => t.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

