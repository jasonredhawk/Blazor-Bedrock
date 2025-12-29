using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasKey(ur => new { ur.UserId, ur.RoleId, ur.TenantId });
        
        builder.HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(ur => ur.Tenant)
            .WithMany(t => t.UserRoles)
            .HasForeignKey(ur => ur.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(ur => new { ur.TenantId, ur.UserId });
        builder.HasIndex(ur => ur.UserId);
    }
}

