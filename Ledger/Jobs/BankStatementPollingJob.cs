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
            try
            {
                await PollOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling bank statement");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollOnce(CancellationToken ct)
    {
        var entries = _bank.GetUnprocessed();
        if (entries.Count == 0) return;

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Direction == StatementDirection.Outgoing)
                    await HandleOutgoing(db, entry, ct);
                else
                    await HandleIncoming(db, entry, ct);

                _bank.MarkProcessed(entry.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process statement entry {EntryId}", entry.Id);
            }
        }
    }

    private async Task HandleOutgoing(LedgerDbContext db, BankStatementEntry entry, CancellationToken ct)
    {
        if (!Guid.TryParse(entry.Reference, out var txId))
        {
            _logger.LogWarning("Outgoing entry {EntryId} has unparseable reference '{Ref}'", entry.Id, entry.Reference);
            return;
        }

        var tx = await db.Transactions
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null)
        {
            _logger.LogWarning("No transaction for outgoing reference {TxId}", txId);
            return;
        }

        switch (tx.Type)
        {
            case TransactionType.Withdrawal when tx.Status == TransactionStatus.Processing:
                foreach (var e in JournalEntryFactory.WithdrawalToSettled(tx)) tx.Entries.Add(e);
                tx.Status = TransactionStatus.Settled;
                tx.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Withdrawal {TxId} settled", tx.Id);
                break;

            case TransactionType.Deposit when tx.Status == TransactionStatus.Processing
                                              && tx.ReviewReason is not null:
                // This is a bounce settlement (rejected deposit being refunded)
                foreach (var e in JournalEntryFactory.BounceSettled(tx)) tx.Entries.Add(e);
                tx.Status = TransactionStatus.Failed;
                tx.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation("Bounce {TxId} settled", tx.Id);
                break;

            default:
                _logger.LogWarning("Unexpected outgoing match — TxId={TxId} Type={Type} Status={Status}",
                    tx.Id, tx.Type, tx.Status);
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleIncoming(LedgerDbContext db, BankStatementEntry entry, CancellationToken ct)
    {
        // Decide: clean path or review path?
        var customer = _customers.FindByIban(entry.CounterpartyIban);
        var forcedReview = entry.ForceReviewReason;

        if (forcedReview is null && customer is not null)
        {
            // Clean deposit — book straight to Settled
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Type = TransactionType.Deposit,
                Status = TransactionStatus.Settled,
                CustomerId = customer.Id,
                Amount = entry.Amount,
                Currency = entry.Currency,
                SepaType = SepaType.Instant,
                ExternalRef = entry.Id.ToString(),
                CounterpartyIban = entry.CounterpartyIban,
                CounterpartyName = entry.CounterpartyName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            foreach (var e in JournalEntryFactory.DepositSettled(tx)) tx.Entries.Add(e);
            db.Transactions.Add(tx);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deposit {TxId} settled clean for customer {CustomerId}", tx.Id, customer.Id);
        }
        else
        {
            // Review path — customer is uncertain, book to review suspense
            var reason = forcedReview ?? ReviewReason.IbanNotOnFile;
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Type = TransactionType.Deposit,
                Status = TransactionStatus.Processing,
                CustomerId = null, // option A — honest about not knowing
                Amount = entry.Amount,
                Currency = entry.Currency,
                SepaType = SepaType.Instant,
                ExternalRef = entry.Id.ToString(),
                CounterpartyIban = entry.CounterpartyIban,
                CounterpartyName = entry.CounterpartyName,
                ReviewReason = reason,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            foreach (var e in JournalEntryFactory.DepositToReview(tx)) tx.Entries.Add(e);
            db.Transactions.Add(tx);
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("Deposit {TxId} routed to review ({Reason})", tx.Id, reason);
        }
    }
}