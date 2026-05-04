using Ledger.Domain;

namespace Ledger.Ledger;

public interface IPostingEngine
{
    /// <summary>
    /// Raises an event for a transaction and produces the corresponding journal entries.
    /// The event is attached to tx.Events; the entries are attached to tx.Entries.
    /// Caller is responsible for SaveChanges.
    /// </summary>
    AccountingEvent RaiseEvent(Transaction tx, string eventType, object? payload = null);
}

public class PostingEngine : IPostingEngine
{
    public AccountingEvent RaiseEvent(Transaction tx, string eventType, object? payload = null)
    {

        var templates = PostingRules.For(eventType);
        
        var now = DateTime.UtcNow;
        var evt = new AccountingEvent
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            EventType = eventType,
            OccurredAt = now,
            CreatedAt = now,
            PayloadJson = payload is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(payload)
        };
        
        tx.Events.Add(evt);
        
        foreach (var t in templates)
        {
            tx.Entries.Add(new JournalEntry
            {
                TransactionId = tx.Id,
                EventId = evt.Id,
                AccountNumber = t.AccountNumber,
                Amount = tx.Amount,
                Direction = t.Direction,
                CustomerId = t.CarriesCustomerId ? tx.CustomerId : null,
                PostedAt = now
            });
        }

        return evt;
    }
}