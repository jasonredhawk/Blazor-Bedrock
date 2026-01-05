using Blazor_Bedrock.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Blazor_Bedrock.Data.Configurations;

public class ChatGptQuestionGroupConfiguration : IEntityTypeConfiguration<ChatGptQuestionGroup>
{
    public void Configure(EntityTypeBuilder<ChatGptQuestionGroup> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(200);
        builder.Property(g => g.Description).HasMaxLength(1000);
        builder.Property(g => g.Order).IsRequired();
        
        builder.HasOne(g => g.Tenant)
            .WithMany()
            .HasForeignKey(g => g.TenantId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasIndex(g => g.TenantId);
        builder.HasIndex(g => new { g.TenantId, g.Order });
    }
}
