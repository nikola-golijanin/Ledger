using Ledger.Domain;
using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Ledger;

public interface ICorrectionService
{
    Task<PostCorrectionResult> PostAsync(PostCorrectionRequest request, CancellationToken ct);
}

public record PostCorrectionRequest(
    string Reason,
    string Description,
    string RequestedBy,
    string Currency,
    Guid? CorrectsTransactionId,
    string? IdempotencyKey,
    IReadOnlyList<CorrectionLineSpec> Lines);

public record PostCorrectionResult(
    Guid TransactionId,
    Guid EventId,
    int LineCount,
    decimal TotalDebit,
    decimal TotalCredit,
    bool ReturnedFromIdempotencyCache);

public class CorrectionService : ICorrectionService
{
    private readonly LedgerDbContext _db;

    public CorrectionService(LedgerDbContext db) => _db = db;

    public async Task<PostCorrectionResult> PostAsync(PostCorrectionRequest request, CancellationToken ct)
    {
        // Idempotency short-circuit
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Transactions
                .Include(t => t.Events)
                .Include(t => t.Entries)
                .FirstOrDefaultAsync(t => t.IdempotencyKey == request.IdempotencyKey, ct);

            if (existing is not null)
            {
                var debit = existing.Entries.Where(e => e.Direction == 1).Sum(e => e.Amount);
                var credit = existing.Entries.Where(e => e.Direction == -1).Sum(e => e.Amount);
                return new PostCorrectionResult(
                    existing.Id,
                    existing.Events.First().Id,
                    existing.Entries.Count,
                    debit,
                    credit,
                    ReturnedFromIdempotencyCache: true);
            }
        }

        var now = DateTime.UtcNow;
        var totalDebit = request.Lines.Where(l => l.Direction == 1).Sum(l => l.Amount);

        // Build the transaction
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = TransactionType.Correction,
            Status = TransactionStatus.Settled,
            CustomerId = null, // corrections may touch many customers; per-line customer_id holds attribution
            Amount = totalDebit, // sum of debits = sum of credits since balanced
            Currency = "EUR",
            CorrectionReason = request.Reason,
            CorrectionDescription = request.Description,
            RequestedBy = request.RequestedBy,
            CorrectsTransactionId = request.CorrectsTransactionId,
            IdempotencyKey = request.IdempotencyKey,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Build the single accounting event for this correction
        var evt = new AccountingEvent
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            EventType = EventTypes.ManualCorrectionPosted,
            PostingRuleId = null,
            OccurredAt = now,
            CreatedAt = now,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                request.Reason,
                request.Description,
                request.RequestedBy,
                request.CorrectsTransactionId
            })
        };

        tx.Events.Add(evt);

        // Build the journal entries directly from the request lines
        foreach (var line in request.Lines)
        {
            tx.Entries.Add(new JournalEntry
            {
                TransactionId = tx.Id,
                EventId = evt.Id,
                AccountNumber = line.AccountNumber,
                Amount = line.Amount,
                Direction = line.Direction,
                CustomerId = line.CustomerId,
                PostedAt = now,
            });
        }

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        return new PostCorrectionResult(
            tx.Id,
            evt.Id,
            request.Lines.Count,
            totalDebit,
            totalDebit, // == credit since balanced
            ReturnedFromIdempotencyCache: false);
    }
}