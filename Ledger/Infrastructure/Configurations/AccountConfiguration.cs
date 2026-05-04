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
        builder.Property(a => a.Number).HasColumnName("number").ValueGeneratedNever();

        builder.Property(a => a.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(a => a.Name).HasColumnName("name").HasMaxLength(128).IsRequired();

        builder.Property(a => a.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.ParentNumber).HasColumnName("parent_number");
        builder.Property(a => a.IsPostable).HasColumnName("is_postable").IsRequired();

        builder.HasOne(a => a.Parent)
            .WithMany()
            .HasForeignKey(a => a.ParentNumber)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Code).IsUnique();
    }
}
