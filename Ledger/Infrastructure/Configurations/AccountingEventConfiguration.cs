using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ledger.Infrastructure.Configurations;

public class AccountingEventConfiguration : IEntityTypeConfiguration<AccountingEvent>
{
    public void Configure(EntityTypeBuilder<AccountingEvent> builder)
    {
        builder.ToTable("accounting_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TransactionId).HasColumnName("transaction_id").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");

        builder.HasMany(e => e.Entries)
            .WithOne(je => je.Event)
            .HasForeignKey(je => je.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TransactionId);
        builder.HasIndex(e => e.EventType);
    }
}