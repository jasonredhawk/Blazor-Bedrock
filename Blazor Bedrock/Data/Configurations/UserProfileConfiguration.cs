using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Bio).HasMaxLength(2000);
        builder.Property(p => p.AvatarUrl).HasMaxLength(500);
        builder.Property(p => p.PhoneNumber).HasMaxLength(50);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.State).HasMaxLength(100);
        builder.Property(p => p.ZipCode).HasMaxLength(20);
        builder.Property(p => p.Country).HasMaxLength(100);
        
        builder.HasOne(p => p.User)
            .WithOne(u => u.UserProfile)
            .HasForeignKey<UserProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(p => p.UserId).IsUnique();
    }
}

