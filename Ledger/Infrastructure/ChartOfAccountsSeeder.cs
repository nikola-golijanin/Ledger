using Ledger.Domain;

namespace Ledger.Infrastructure;

public static class ChartOfAccountsSeeder
{
    private record AccountSeed(int Number, string Code, string Name, AccountType Type, int? Parent, bool IsPostable);

    private static readonly AccountSeed[] Seeds =
    [
        // Assets
        new(1100, "rollup.cash_and_bank",        "Cash & Bank",                   AccountType.Asset,     null, false),
        new(1110, "bank.pooling",                "Bank – Pooling",                AccountType.Asset,     1100, true),
        new(1120, "bank.treasury",               "Bank – Treasury",               AccountType.Asset,     1100, true),

        new(1150, "rollup.suspense_assets",      "Suspense (Assets)",             AccountType.Asset,     null, false),
        new(1151, "suspense.deposit.inflight",   "Suspense – Deposit In-flight",  AccountType.Asset,     1150, true),
        new(1160, "suspense.treasury_out",       "Suspense – Treasury Out",       AccountType.Asset,     1150, true),
        new(1170, "suspense.treasury_in",        "Suspense – Treasury In",        AccountType.Asset,     1150, true),

        new(1300, "rollup.investments",          "Investments",                   AccountType.Asset,     null, false),
        new(1310, "market",                      "Market",                        AccountType.Asset,     1300, true),

        // Liabilities
        new(2100, "rollup.customer_obligations", "Customer Obligations",          AccountType.Liability, null, false),
        new(2110, "customer.viban",              "Customer – vIBAN",              AccountType.Liability, 2100, true),

        new(2200, "rollup.suspense_liabilities", "Suspense (Liabilities)",        AccountType.Liability, null, false),
        new(2210, "suspense.withdrawal",         "Suspense – Withdrawal",         AccountType.Liability, 2200, true),
        new(2220, "suspense.deposit.review",     "Suspense – Deposit Review",     AccountType.Liability, 2200, true),
        new(2230, "suspense.bounce",             "Suspense – Bounce",             AccountType.Liability, 2200, true),

        // Equity
        new(3100, "retained_earnings",           "Retained Earnings",             AccountType.Equity,    null, true),
        new(3200, "paid_in_capital",             "Paid-in Capital",               AccountType.Equity,    null, true),

        // Revenue
        new(4100, "fee_income.deposit",          "Fee Income – Deposit",          AccountType.Revenue,   null, true),
        new(4200, "fee_income.withdrawal",       "Fee Income – Withdrawal",       AccountType.Revenue,   null, true),
        new(4300, "interest_income",             "Interest Income",               AccountType.Revenue,   null, true),

        // Expenses
        new(5100, "bank_fees",                   "Bank Fees",                     AccountType.Expense,   null, true),
        new(5200, "operational_expenses",        "Operational Expenses",          AccountType.Expense,   null, true),
    ];

    public static async Task SeedAsync(LedgerDbContext db, CancellationToken ct = default)
    {
        // Insert parents first to satisfy FK constraints (sort by Parent IS NULL desc)
        foreach (var s in Seeds.OrderBy(s => s.Parent ?? -1))
        {
            var existing = await db.Accounts.FindAsync([s.Number], ct);
            if (existing is null)
            {
                db.Accounts.Add(new Account
                {
                    Number = s.Number,
                    Code = s.Code,
                    Name = s.Name,
                    Type = s.Type,
                    ParentNumber = s.Parent,
                    IsPostable = s.IsPostable
                });
            }
            else
            {
                existing.Code = s.Code;
                existing.Name = s.Name;
                existing.Type = s.Type;
                existing.ParentNumber = s.Parent;
                existing.IsPostable = s.IsPostable;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}