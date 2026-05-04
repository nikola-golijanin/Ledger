namespace Ledger.Domain;

public class Transaction
{
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public Guid? CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? ExternalRef { get; set; }
    public SepaType? SepaType { get; set; }

    public string? CounterpartyIban { get; set; }
    public string? CounterpartyName { get; set; }

    public ReviewReason? ReviewReason { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<JournalEntry> Entries { get; set; } = new List<JournalEntry>();
    public ICollection<AccountingEvent> Events { get; set; } = new List<AccountingEvent>();
}