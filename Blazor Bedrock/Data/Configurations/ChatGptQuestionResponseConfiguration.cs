using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptQuestionResponseConfiguration : IEntityTypeConfiguration<ChatGptQuestionResponse>
{
    public void Configure(EntityTypeBuilder<ChatGptQuestionResponse> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Response).IsRequired().HasColumnType("LONGTEXT");
        
        builder.HasOne(r => r.Conversation)
            .WithMany()
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(r => r.Question)
            .WithMany(q => q.Responses)
            .HasForeignKey(r => r.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(r => r.Document)
            .WithMany()
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasOne(r => r.Prompt)
            .WithMany()
            .HasForeignKey(r => r.PromptId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasIndex(r => r.ConversationId);
        builder.HasIndex(r => r.QuestionId);
        builder.HasIndex(r => r.DocumentId);
        builder.HasIndex(r => new { r.ConversationId, r.QuestionId, r.DocumentId });
    }
}
