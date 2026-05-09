using Ledger.Infrastructure;

namespace Ledger.Ledger;
using Microsoft.EntityFrameworkCore;


public interface IPostingRuleDiffService
{
    Task<DiffReport> DiffAsync(string eventType, DateTime? from, DateTime? to, CancellationToken ct);
}

public record DiffReport(
    string EventType,
    int CurrentRuleVersion,
    int TotalEvents,
    int DifferingEvents,
    IReadOnlyList<DiffEventRow> Events);

public record DiffEventRow(
    Guid EventId,
    Guid TransactionId,
    DateTime OccurredAt,
    decimal Amount,
    Guid? CustomerId,
    int RuleVersionUsed,
    bool Differs,
    IReadOnlyList<EntryShape> Actual,
    IReadOnlyList<EntryShape> Expected);

public record EntryShape(
    int AccountNumber,
    short Direction,
    decimal Amount,
    Guid? CustomerId);

public class PostingRuleDiffService : IPostingRuleDiffService
{
    private readonly LedgerDbContext _db;

    public PostingRuleDiffService(LedgerDbContext db) => _db = db;

    public async Task<DiffReport> DiffAsync(
        string eventType,
        DateTime? from,
        DateTime? to,
        CancellationToken ct)
    {
        // Load the current active rule (the comparison baseline)
        var activeRule = await _db.PostingRules
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.EventType == eventType && r.IsActive, ct)
            ?? throw new InvalidOperationException($"No active posting rule for event type '{eventType}'.");

        // Load past events with their entries, transactions, and the rule that produced them
        var query = _db.AccountingEvents
            .AsNoTracking()
            .Where(e => e.EventType == eventType);

        if (from is not null) query = query.Where(e => e.OccurredAt >= from);
        if (to   is not null) query = query.Where(e => e.OccurredAt <= to);

        var events = await query
            .Include(e => e.Transaction)
            .Include(e => e.PostingRule)
            .Include(e => e.Entries)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(ct);

        var rows = new List<DiffEventRow>(events.Count);
        var differingCount = 0;

        foreach (var evt in events)
        {
            var expected = ExpectedEntriesCalculator.Compute(activeRule, evt.Transaction);
            var actual = evt.Entries
                .Select(e => new EntryShape(e.AccountNumber, e.Direction, e.Amount, e.CustomerId))
                .ToList();

            var differs = !EntriesMatch(actual, expected);
            if (differs) differingCount++;

            rows.Add(new DiffEventRow(
                EventId: evt.Id,
                TransactionId: evt.TransactionId,
                OccurredAt: evt.OccurredAt,
                Amount: evt.Transaction.Amount,
                CustomerId: evt.Transaction.CustomerId,
                RuleVersionUsed: evt.PostingRule?.Version ?? 0,  // 0 indicates "no rule" (corrections etc.)
                Differs: differs,
                Actual: actual,
                Expected: expected.Select(x => new EntryShape(x.AccountNumber, x.Direction, x.Amount, x.CustomerId)).ToList()));
        }

        return new DiffReport(
            EventType: eventType,
            CurrentRuleVersion: activeRule.Version,
            TotalEvents: events.Count,
            DifferingEvents: differingCount,
            Events: rows);
    }

    /// <summary>
    /// Two entry sets match if they contain the same (account, direction, amount, customer_id) tuples,
    /// regardless of ordering. We use a dictionary count for deterministic comparison.
    /// </summary>
    private static bool EntriesMatch(IReadOnlyList<EntryShape> a, IReadOnlyList<ExpectedEntry> b)
    {
        if (a.Count != b.Count) return false;

        var aKeys = a.Select(x => (x.AccountNumber, x.Direction, x.Amount, x.CustomerId))
                     .OrderBy(k => k.AccountNumber).ThenBy(k => k.Direction).ThenBy(k => k.Amount).ThenBy(k => k.CustomerId)
                     .ToList();
        var bKeys = b.Select(x => (x.AccountNumber, x.Direction, x.Amount, x.CustomerId))
                     .OrderBy(k => k.AccountNumber).ThenBy(k => k.Direction).ThenBy(k => k.Amount).ThenBy(k => k.CustomerId)
                     .ToList();

        return aKeys.SequenceEqual(bKeys);
    }
}