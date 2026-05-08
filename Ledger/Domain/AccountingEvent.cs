namespace Ledger.Domain;

public class AccountingEvent
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public string EventType { get; set; } = default!;
    public Guid? PostingRuleId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PayloadJson { get; set; }

    public Transaction Transaction { get; set; } = default!;
    public PostingRule? PostingRule { get; set; } = default!;
    public ICollection<JournalEntry> Entries { get; set; } = new List<JournalEntry>();
}