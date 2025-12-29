using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptApiKeyConfiguration : IEntityTypeConfiguration<ChatGptApiKey>
{
    public void Configure(EntityTypeBuilder<ChatGptApiKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.EncryptedApiKey).IsRequired().HasMaxLength(1000);
        builder.Property(k => k.PreferredModel).HasMaxLength(100);
        
        builder.HasOne(k => k.User)
            .WithMany(u => u.ChatGptApiKeys)
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(k => k.Tenant)
            .WithMany()
            .HasForeignKey(k => k.TenantId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasIndex(k => new { k.UserId, k.TenantId });
    }
}

