using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Configurations;

public class PostingRuleConfiguration : IEntityTypeConfiguration<PostingRule>
{
    public void Configure(EntityTypeBuilder<PostingRule> builder)
    {
        builder.ToTable("posting_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasMaxLength(256);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasMany(r => r.Lines)
            .WithOne(l => l.PostingRule)
            .HasForeignKey(l => l.PostingRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.EventType).IsUnique();
    }
}