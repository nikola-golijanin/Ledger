namespace Ledger.Domain;

/// <summary>
/// Well-known leaf account numbers. Rollup parents live in code as separate constants
/// when needed for queries, but they don't receive postings.
/// </summary>
public static class AccountNumbers
{
    // Assets — Cash & Bank
    public const int BankPooling = 1110;
    public const int BankTreasury = 1120;

    // Assets — Suspense
    public const int SuspenseDepositInflight = 1151;
    public const int SuspenseTreasuryOut = 1160;
    public const int SuspenseTreasuryIn = 1170;

    // Assets — Investments
    public const int Market = 1310;

    // Liabilities — Customer
    public const int CustomerViban = 2110;

    // Liabilities — Suspense
    public const int SuspenseWithdrawal = 2210;
    public const int SuspenseDepositReview = 2220;
    public const int SuspenseBounce = 2230;

    // Equity
    public const int RetainedEarnings = 3100;
    public const int PaidInCapital = 3200;

    // Revenue
    public const int FeeIncomeDeposit = 4100;
    public const int FeeIncomeWithdrawal = 4200;
    public const int InterestIncome = 4300;

    // Expenses
    public const int BankFees = 5100;
    public const int OperationalExpenses = 5200;
}