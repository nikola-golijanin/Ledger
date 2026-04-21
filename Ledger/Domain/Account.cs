namespace Ledger.Domain;

public class Account
{
    public int Number { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public AccountType Type { get; set; }
    public int? ParentNumber { get; set; }
    public Account? Parent { get; set; }
}

public enum AccountType
{
    Asset = 1,
    Liability = 2
}

public enum TransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    TreasuryToMarket = 3,
    MarketToTreasury = 4
}

public enum TransactionStatus
{
    Pending = 1,
    Processing = 2,
    Settled = 3,
    Failed = 4,
    Reversed = 5
}

/// <summary>
/// Well-known account numbers. Keep in sync with the chart-of-accounts seed data.
/// </summary>
public static class AccountNumbers
{
    // Assets (100-range)
    public const int BankPooling = 110;
    public const int BankTreasury = 120;
    public const int Market = 130;

    // In-flight assets / receivables (150-range)
    public const int SuspenseDeposit = 150;
    public const int SuspenseTreasuryOut = 160;
    public const int SuspenseTreasuryIn = 170;

    // Liabilities (200-range)
    public const int CustomerViban = 210;
    public const int SuspenseWithdrawal = 220;
}

public class Transaction
{
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public Guid? CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? ExternalRef { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public SepaType? SepaType { get; set; }  // nullable: only meaningful for deposits/withdrawals

    public ICollection<JournalEntry> Entries { get; set; } = new List<JournalEntry>();
}

public class JournalEntry
{
    public long Id { get; set; }
    public Guid TransactionId { get; set; }
    public int AccountNumber { get; set; }
    public Guid? CustomerId { get; set; }
    public decimal Amount { get; set; }
    public short Direction { get; set; }
    public DateTime PostedAt { get; set; }

    public Transaction Transaction { get; set; } = default!;
    public Account Account { get; set; } = default!;

    public decimal SignedAmount => Direction * Amount;
}

public enum SepaType
{
    Standard = 1,
    Instant = 2
}