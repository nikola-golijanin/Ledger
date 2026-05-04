using Ledger.Banking;
using Ledger.Customers;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Jobs;

public class BankStatementPollingJob : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly IMockBank _bank;
    private readonly ICustomerRegistry _customers;
    private readonly ILogger<BankStatementPollingJob> _logger;

    public BankStatementPollingJob(
        IServiceProvider services,
        IMockBank bank,
        ICustomerRegistry customers,
        ILogger<BankStatementPollingJob> logger)
    {
        _services = services;
        _bank = bank;
        _customers = customers;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollOnce(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Error polling bank statement"); }
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollOnce(CancellationToken ct)
    {
        var entries = _bank.GetUnprocessed();
        if (entries.Count == 0) return;

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Direction == StatementDirection.Outgoing)
                    await HandleOutgoing(db, posting, entry, ct);
                else
                    await HandleIncoming(db, posting, entry, ct);

                _bank.MarkProcessed(entry.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process statement entry {EntryId}", entry.Id);
            }
        }
    }

    private async Task HandleOutgoing(LedgerDbContext db, IPostingEngine posting, BankStatementEntry entry, CancellationToken ct)
    {
        if (!Guid.TryParse(entry.Reference, out var txId))
        {
            _logger.LogWarning("Outgoing entry {EntryId} has unparseable reference '{Ref}'", entry.Id, entry.Reference);
            return;
        }

        var tx = await db.Transactions
            .Include(t => t.Entries)
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null)
        {
            _logger.LogWarning("No transaction for outgoing reference {TxId}", txId);
            return;
        }

        // Decide which event to raise based on what was previously raised on this tx
        var lastEvent = tx.Events.OrderBy(e => e.OccurredAt).LastOrDefault();

        switch (lastEvent?.EventType)
        {
            case EventTypes.WithdrawalInitiated:
                posting.RaiseEvent(tx, EventTypes.WithdrawalSettled);
                tx.Status = TransactionStatus.Settled;
                tx.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Withdrawal {TxId} settled", tx.Id);
                break;

            case EventTypes.BounceInitiated:
                posting.RaiseEvent(tx, EventTypes.BounceSettled);
                tx.Status = TransactionStatus.Failed;
                tx.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Bounce {TxId} settled", tx.Id);
                break;

            default:
                _logger.LogWarning("Unexpected outgoing match — TxId={TxId} LastEvent={LastEvent}",
                    tx.Id, lastEvent?.EventType);
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleIncoming(LedgerDbContext db, IPostingEngine posting, BankStatementEntry entry, CancellationToken ct)
    {
        var customer = _customers.FindByIban(entry.CounterpartyIban);
        var forcedReview = entry.ForceReviewReason;

        if (forcedReview is null && customer is not null)
        {
            // Clean match
            var tx = NewDepositTransaction(entry, customer.Id, reviewReason: null);
            db.Transactions.Add(tx);
            posting.RaiseEvent(tx, EventTypes.DepositDetectedCleanMatch,
                new { entry.CounterpartyIban, entry.CounterpartyName, customer_id = customer.Id });
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deposit {TxId} settled clean for customer {CustomerId}", tx.Id, customer.Id);
        }
        else
        {
            var reason = forcedReview ?? ReviewReason.IbanNotOnFile;
            var tx = NewDepositTransaction(entry, customerId: null, reviewReason: reason);
            db.Transactions.Add(tx);
            posting.RaiseEvent(tx, EventTypes.DepositDetectedRequiresReview,
                new { entry.CounterpartyIban, entry.CounterpartyName, reason = reason.ToString() });
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("Deposit {TxId} routed to review ({Reason})", tx.Id, reason);
        }
    }

    private static Transaction NewDepositTransaction(BankStatementEntry entry, Guid? customerId, ReviewReason? reviewReason)
    {
        var now = DateTime.UtcNow;
        return new Transaction
        {
            Id = Guid.NewGuid(),
            Type = TransactionType.Deposit,
            Status = customerId is not null ? TransactionStatus.Settled : TransactionStatus.Processing,
            CustomerId = customerId,
            Amount = entry.Amount,
            Currency = entry.Currency,
            SepaType = SepaType.Instant,
            ExternalRef = entry.Id.ToString(),
            CounterpartyIban = entry.CounterpartyIban,
            CounterpartyName = entry.CounterpartyName,
            ReviewReason = reviewReason,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}