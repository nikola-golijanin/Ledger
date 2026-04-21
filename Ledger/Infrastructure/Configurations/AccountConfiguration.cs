using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Number);

        builder.Property(a => a.Number)
            .HasColumnName("number")
            .ValueGeneratedNever();

        builder.Property(a => a.Code)
            .HasColumnName("code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.Name)
            .HasColumnName("name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(a => a.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.ParentNumber)
            .HasColumnName("parent_number");

        builder.HasOne(a => a.Parent)
            .WithMany()
            .HasForeignKey(a => a.ParentNumber)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Code).IsUnique();
    }
}

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(t => t.Amount)
            .HasColumnName("amount")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(t => t.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(t => t.ExternalRef)
            .HasColumnName("external_ref")
            .HasMaxLength(128);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasMany(t => t.Entries)
            .WithOne(e => e.Transaction)
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.Property(t => t.SepaType)
            .HasColumnName("sepa_type")
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(t => t.CounterpartyIban)
            .HasColumnName("counterparty_iban")
            .HasMaxLength(64);

        builder.Property(t => t.CounterpartyName)
            .HasColumnName("counterparty_name")
            .HasMaxLength(128);

        builder.Property(t => t.ReviewReason)
            .HasColumnName("review_reason")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(t => t.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(t => t.ReviewedBy)
            .HasColumnName("reviewed_by")
            .HasMaxLength(128);
        
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.CustomerId);
        builder.HasIndex(t => t.ExternalRef);
    }
}

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

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(e => e.TransactionId)
            .HasColumnName("transaction_id")
            .IsRequired();

        builder.Property(e => e.AccountNumber)
            .HasColumnName("account_number")
            .IsRequired();

        builder.Property(e => e.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(e => e.Amount)
            .HasColumnName("amount")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(e => e.Direction)
            .HasColumnName("direction")
            .IsRequired();

        builder.Property(e => e.PostedAt)
            .HasColumnName("posted_at")
            .IsRequired();

        builder.Ignore(e => e.SignedAmount);

        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountNumber)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TransactionId);
        builder.HasIndex(e => new { e.AccountNumber, e.PostedAt });
        builder.HasIndex(e => e.CustomerId)
            .HasFilter("customer_id IS NOT NULL");
    }
}