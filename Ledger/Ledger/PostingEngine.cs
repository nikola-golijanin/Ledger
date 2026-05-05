using Ledger.Domain;
using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Ledger;

public interface IPostingEngine
{
    /// <summary>
    /// Raises an event for a transaction and produces journal entries from the active posting rule.
    /// Both the event and the entries are attached to tx; caller is responsible for SaveChanges.
    /// </summary>
    Task<AccountingEvent> RaiseEventAsync(
        Transaction tx,
        string eventType,
        object? payload = null,
        CancellationToken ct = default);
}

public class PostingEngine : IPostingEngine
{
    private readonly LedgerDbContext _db;

    public PostingEngine(LedgerDbContext db) => _db = db;

    public async Task<AccountingEvent> RaiseEventAsync(
        Transaction tx,
        string eventType,
        object? payload = null,
        CancellationToken ct = default)
    {
        var rule = await _db.PostingRules
            .Include(r => r.Lines.OrderBy(l => l.Sequence))
            .FirstOrDefaultAsync(r => r.EventType == eventType, ct)
            ?? throw new InvalidOperationException($"No posting rule for event type '{eventType}'");

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
                : System.Text.Json.JsonSerializer.Serialize(payload),
        };

        tx.Events.Add(evt);

        foreach (var line in rule.Lines)
        {
            tx.Entries.Add(new JournalEntry
            {
                TransactionId = tx.Id,
                EventId = evt.Id,
                AccountNumber = line.AccountNumber,
                Amount = tx.Amount,
                Direction = line.Direction,
                CustomerId = line.CarriesCustomerId ? tx.CustomerId : null,
                PostedAt = now,
            });
        }

        return evt;
    }
}