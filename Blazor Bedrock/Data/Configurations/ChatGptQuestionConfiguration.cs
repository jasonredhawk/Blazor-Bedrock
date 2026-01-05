using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptQuestionConfiguration : IEntityTypeConfiguration<ChatGptQuestion>
{
    public void Configure(EntityTypeBuilder<ChatGptQuestion> builder)
    {
        builder.HasKey(q => q.Id);
        builder.Property(q => q.QuestionText).IsRequired().HasMaxLength(2000);
        builder.Property(q => q.Description).HasMaxLength(1000);
        builder.Property(q => q.Order).IsRequired();
        builder.Property(q => q.IsActive).IsRequired();
        
        builder.HasOne(q => q.Tenant)
            .WithMany()
            .HasForeignKey(q => q.TenantId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasOne(q => q.Group)
            .WithMany(g => g.Questions)
            .HasForeignKey(q => q.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasIndex(q => q.TenantId);
        builder.HasIndex(q => q.GroupId);
        builder.HasIndex(q => new { q.TenantId, q.GroupId, q.Order });
    }
}
