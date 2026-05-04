namespace Ledger.Domain;

public class AccountingEvent
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string EventType { get; set; } = default!;
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PayloadJson { get; set; }    // free-form context, JSONB

    public Transaction Transaction { get; set; } = default!;
    public ICollection<JournalEntry> Entries { get; set; } = new List<JournalEntry>();
}