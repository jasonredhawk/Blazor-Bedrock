using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.HasKey(ts => ts.Id);
        builder.Property(ts => ts.Status).IsRequired().HasMaxLength(50).HasDefaultValue("active");
        builder.Property(ts => ts.StripeSubscriptionId).HasMaxLength(200);
        builder.Property(ts => ts.StripeCustomerId).HasMaxLength(200);
        
        builder.HasOne(ts => ts.Tenant)
            .WithOne(t => t.CurrentSubscription)
            .HasForeignKey<TenantSubscription>(ts => ts.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(ts => ts.SubscriptionPlan)
            .WithMany(sp => sp.TenantSubscriptions)
            .HasForeignKey(ts => ts.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
            
        builder.HasIndex(ts => ts.TenantId).IsUnique();
        builder.HasIndex(ts => ts.StripeSubscriptionId);
    }
}
