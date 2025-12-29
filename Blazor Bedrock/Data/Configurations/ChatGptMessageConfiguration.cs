using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptMessageConfiguration : IEntityTypeConfiguration<ChatGptMessage>
{
    public void Configure(EntityTypeBuilder<ChatGptMessage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Content).HasColumnType("LONGTEXT");
        builder.HasIndex(m => m.ConversationId);
    }
}

