using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal_entries", t =>
        {
            t.HasCheckConstraint("ck_journal_entries_amount_positive", "amount > 0");
            t.HasCheckConstraint("ck_journal_entries_direction", "direction IN (-1, 1)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(e => e.TransactionId).HasColumnName("transaction_id").IsRequired();
        builder.Property(e => e.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(e => e.AccountNumber).HasColumnName("account_number").IsRequired();
        builder.Property(e => e.CustomerId).HasColumnName("customer_id");
        builder.Property(e => e.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired();
        builder.Property(e => e.Direction).HasColumnName("direction").IsRequired();
        builder.Property(e => e.PostedAt).HasColumnName("posted_at").IsRequired();

        builder.Ignore(e => e.SignedAmount);

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountNumber)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TransactionId);
        builder.HasIndex(e => e.EventId);
        builder.HasIndex(e => new { e.AccountNumber, e.PostedAt });
        builder.HasIndex(e => e.CustomerId).HasFilter("customer_id IS NOT NULL");
    }
}