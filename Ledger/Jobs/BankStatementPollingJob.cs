using Ledger.Banking;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Jobs;

public partial class BankStatementPollingJob : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly IMockBank _bank;
    private readonly ILogger<BankStatementPollingJob> _logger;

    public BankStatementPollingJob(
        IServiceProvider services,
        IMockBank bank,
        ILogger<BankStatementPollingJob> logger)
    {
        _services = services;
        _bank = bank;
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
                LogFailedToProcessStatementEntryEntryid(entry.Id, ex);
            }
        }
    }

    private async Task HandleOutgoing(LedgerDbContext db, BankStatementEntry entry, CancellationToken ct)
    {
        // Outgoing = a withdrawal we initiated; match by reference
        if (!Guid.TryParse(entry.Reference, out var txId))
        {
            _logger.LogWarning("Outgoing entry {EntryId} has unparseable reference '{Reference}'",
                entry.Id, entry.Reference);
            return;
        }

        var tx = await db.Transactions
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null)
        {
            _logger.LogWarning("No transaction found for outgoing reference {TxId}", txId);
            return;
        }

        if (tx.Status != TransactionStatus.Processing)
        {
            _logger.LogWarning("Transaction {TxId} in unexpected status {Status} for settlement",
                tx.Id, tx.Status);
            return;
        }

        foreach (var e in JournalEntryFactory.WithdrawalToSettled(tx))
            tx.Entries.Add(e);

        tx.Status = TransactionStatus.Settled;
        tx.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        LogWithdrawalTxidSettled(tx.Id);
    }

    private async Task HandleIncoming(LedgerDbContext db, BankStatementEntry entry, CancellationToken ct)
    {
        // Incoming = a customer deposit. No prior transaction — we create one directly in SETTLED.
        // Fake customer id for now (we'll look it up by IBAN later).
        var fakeCustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = TransactionType.Deposit,
            Status = TransactionStatus.Settled,
            CustomerId = fakeCustomerId,
            Amount = entry.Amount,
            Currency = entry.Currency,
            ExternalRef = entry.Id.ToString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var e in JournalEntryFactory.DepositSettled(tx))
            tx.Entries.Add(e);

        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);

        LogDepositTxidBookedFromStatementEntryEntryid(tx.Id, entry.Id);
    }

    [LoggerMessage(LogLevel.Information, "Deposit {TxId} booked from statement entry {EntryId}")]
    partial void LogDepositTxidBookedFromStatementEntryEntryid(Guid txId, Guid entryId);

    [LoggerMessage(LogLevel.Error, "Failed to process statement entry {EntryId}")]
    partial void LogFailedToProcessStatementEntryEntryid(Guid entryId, Exception exception);

    [LoggerMessage(LogLevel.Information, "Withdrawal {TxId} settled")]
    partial void LogWithdrawalTxidSettled(Guid txId);
}