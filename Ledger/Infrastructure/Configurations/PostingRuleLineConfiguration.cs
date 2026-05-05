using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Configurations;

public class PostingRuleLineConfiguration : IEntityTypeConfiguration<PostingRuleLine>
{
    public void Configure(EntityTypeBuilder<PostingRuleLine> builder)
    {
        builder.ToTable("posting_rule_lines", t =>
        {
            t.HasCheckConstraint("ck_posting_rule_lines_direction", "direction IN (-1, 1)");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(l => l.PostingRuleId).HasColumnName("posting_rule_id").IsRequired();
        builder.Property(l => l.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(l => l.AccountNumber).HasColumnName("account_number").IsRequired();
        builder.Property(l => l.Direction).HasColumnName("direction").IsRequired();
        builder.Property(l => l.CarriesCustomerId).HasColumnName("carries_customer_id").IsRequired();

        builder.HasOne(l => l.Account)
            .WithMany()
            .HasForeignKey(l => l.AccountNumber)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.PostingRuleId);
        builder.HasIndex(l => new { l.PostingRuleId, l.Sequence }).IsUnique();
    }
}