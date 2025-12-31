using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptConversationConfiguration : IEntityTypeConfiguration<ChatGptConversation>
{
    public void Configure(EntityTypeBuilder<ChatGptConversation> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).IsRequired().HasMaxLength(500);
        builder.Property(c => c.Model).HasMaxLength(100);
        builder.Property(c => c.SelectedSheetNames).HasMaxLength(2000); // JSON array of sheet names
        
        builder.HasOne(c => c.User)
            .WithMany(u => u.ChatGptConversations)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(c => new { c.UserId, c.TenantId });
    }
}

