using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class StripeSubscriptionConfiguration : IEntityTypeConfiguration<StripeSubscription>
{
    public void Configure(EntityTypeBuilder<StripeSubscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.StripeSubscriptionId).IsRequired().HasMaxLength(200);
        builder.Property(s => s.StripeCustomerId).IsRequired().HasMaxLength(200);
        builder.Property(s => s.PlanName).HasMaxLength(200);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Currency).HasMaxLength(10);
        
        builder.HasOne(s => s.Tenant)
            .WithMany(t => t.StripeSubscriptions)
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(s => s.TenantId);
        builder.HasIndex(s => s.StripeSubscriptionId).IsUnique();
    }
}

