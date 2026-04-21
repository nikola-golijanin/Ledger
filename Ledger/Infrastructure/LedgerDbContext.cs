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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ledger");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }
}

public static class ChartOfAccountsSeeder
{
    private static readonly Account[] Accounts =
    [
        new() { Number = AccountNumbers.BankPooling,         Code = "bank.pooling",          Name = "Bank – Pooling",          Type = AccountType.Asset },
        new() { Number = AccountNumbers.BankTreasury,        Code = "bank.treasury",         Name = "Bank – Treasury",         Type = AccountType.Asset },
        new() { Number = AccountNumbers.Market,              Code = "market",                Name = "Market",                  Type = AccountType.Asset },

        new() { Number = AccountNumbers.SuspenseDeposit,     Code = "suspense.deposit",      Name = "Suspense – Deposit",      Type = AccountType.Asset },
        new() { Number = AccountNumbers.SuspenseTreasuryOut, Code = "suspense.treasury_out", Name = "Suspense – Treasury Out", Type = AccountType.Asset },
        new() { Number = AccountNumbers.SuspenseTreasuryIn,  Code = "suspense.treasury_in",  Name = "Suspense – Treasury In",  Type = AccountType.Asset },

        new() { Number = AccountNumbers.CustomerViban,       Code = "customer.viban",        Name = "Customer – vIBAN",        Type = AccountType.Liability },
        new() { Number = AccountNumbers.SuspenseWithdrawal,  Code = "suspense.withdrawal",   Name = "Suspense – Withdrawal",   Type = AccountType.Liability }
    ];

    public static async Task SeedAsync(LedgerDbContext db, CancellationToken ct = default)
    {
        foreach (var account in Accounts)
        {
            var existing = await db.Accounts.FindAsync([account.Number], ct);
            if (existing is null)
            {
                db.Accounts.Add(account);
            }
            else
            {
                existing.Code = account.Code;
                existing.Name = account.Name;
                existing.Type = account.Type;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}