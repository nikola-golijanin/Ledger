using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.CustomerId).HasColumnName("customer_id");
        builder.Property(t => t.Amount).HasColumnName("amount").HasPrecision(19, 4).IsRequired();
        builder.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(t => t.ExternalRef).HasColumnName("external_ref").HasMaxLength(128);

        builder.Property(t => t.SepaType).HasColumnName("sepa_type").HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.CounterpartyIban).HasColumnName("counterparty_iban").HasMaxLength(64);
        builder.Property(t => t.CounterpartyName).HasColumnName("counterparty_name").HasMaxLength(128);

        builder.Property(t => t.ReviewReason).HasColumnName("review_reason").HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(t => t.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(128);

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasMany(t => t.Entries)
            .WithOne(e => e.Transaction)
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Events)
            .WithOne(e => e.Transaction)
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.CustomerId);
        builder.HasIndex(t => t.ExternalRef);
    }
}