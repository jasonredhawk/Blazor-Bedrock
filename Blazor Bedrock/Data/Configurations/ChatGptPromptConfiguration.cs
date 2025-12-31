using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptPromptConfiguration : IEntityTypeConfiguration<ChatGptPrompt>
{
    public void Configure(EntityTypeBuilder<ChatGptPrompt> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PromptText).HasColumnType("LONGTEXT");
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.PromptType)
            .IsRequired()
            .HasConversion<int>(); // Store as int in database
        
        builder.HasOne(p => p.Tenant)
            .WithMany(t => t.ChatGptPrompts)
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => p.PromptType);
    }
}

