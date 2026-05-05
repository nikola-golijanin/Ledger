using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure;


public class LedgerDbContext : DbContext
{
    public LedgerDbContext(DbContextOptions<LedgerDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<AccountingEvent> AccountingEvents => Set<AccountingEvent>();
    public DbSet<PostingRule> PostingRules => Set<PostingRule>();
    public DbSet<PostingRuleLine> PostingRuleLines => Set<PostingRuleLine>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ledger");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }
}