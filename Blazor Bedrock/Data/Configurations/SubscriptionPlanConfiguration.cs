using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.HasKey(sp => sp.Id);
        builder.Property(sp => sp.Name).IsRequired().HasMaxLength(100);
        builder.Property(sp => sp.Description).HasMaxLength(1000);
        builder.Property(sp => sp.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("USD");
        builder.Property(sp => sp.StripeMonthlyPriceId).HasMaxLength(200);
        builder.Property(sp => sp.StripeYearlyPriceId).HasMaxLength(200);
        builder.HasIndex(sp => sp.Name).IsUnique();
    }
}
