namespace Ledger.Domain;

public class JournalEntry
{
    public long Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid EventId { get; set; }              // NEW — every entry traces to an event
    public int AccountNumber { get; set; }
    public Guid? CustomerId { get; set; }
    public decimal Amount { get; set; }
    public short Direction { get; set; }
    public DateTime PostedAt { get; set; }

    public Transaction Transaction { get; set; } = default!;
    public AccountingEvent Event { get; set; } = default!;
    public Account Account { get; set; } = default!;

    public decimal SignedAmount => Direction * Amount;
}