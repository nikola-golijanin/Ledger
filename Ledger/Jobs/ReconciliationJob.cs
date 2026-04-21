using Ledger.Domain;
using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Jobs;

public class ReconciliationJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(
        IServiceProvider services,
        ILogger<ReconciliationJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckOnce(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Reconciliation failed"); }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckOnce(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        // Sum signed amounts per account.
        var balances = await db.JournalEntries
            .GroupBy(e => e.AccountNumber)
            .Select(g => new
            {
                Account = g.Key,
                Signed = g.Sum(e => (decimal)e.Direction * e.Amount)
            })
            .ToDictionaryAsync(x => x.Account, x => x.Signed, ct);

        // Pull the values we care about, defaulting to zero if the account has no entries yet.
        decimal Get(int acc) => balances.TryGetValue(acc, out var v) ? v : 0m;

        var pooling            = Get(AccountNumbers.BankPooling);            // asset: positive when we have money
        var customerSigned     = Get(AccountNumbers.CustomerViban);          // liability: negative when we owe
        var suspenseDeposit    = Get(AccountNumbers.SuspenseDeposit);        // asset
        var suspenseWithdrawal = Get(AccountNumbers.SuspenseWithdrawal);     // liability: negative when we have an in-flight obligation

        // Flip liability signs to natural (positive) values for readability
        var customerOwed       = -customerSigned;
        var withdrawalInFlight = -suspenseWithdrawal;

        // Core invariant:
        //   bank.pooling  +  suspense.deposit  ==  customer.viban (natural)  +  suspense.withdrawal (natural)
        // Rearranged, the drift should be zero:
        var drift = pooling + suspenseDeposit - customerOwed - withdrawalInFlight;

        if (drift != 0m)
        {
            _logger.LogError(
                "RECONCILIATION DRIFT detected: Drift={Drift} | pooling={Pooling} suspenseDeposit={SuspenseDeposit} customerOwed={CustomerOwed} withdrawalInFlight={WithdrawalInFlight}",
                drift, pooling, suspenseDeposit, customerOwed, withdrawalInFlight);
        }
        else
        {
            _logger.LogInformation(
                "Reconciliation OK | pooling={Pooling} customerOwed={CustomerOwed} suspenseDeposit={SuspenseDeposit} withdrawalInFlight={WithdrawalInFlight}",
                pooling, customerOwed, suspenseDeposit, withdrawalInFlight);
        }

        // Secondary check: every transaction's signed sum should be exactly zero.
        var imbalancedTx = await db.JournalEntries
            .GroupBy(e => e.TransactionId)
            .Select(g => new
            {
                TxId = g.Key,
                Sum = g.Sum(e => (decimal)e.Direction * e.Amount)
            })
            .Where(x => x.Sum != 0m)
            .ToListAsync(ct);

        foreach (var bad in imbalancedTx)
        {
            _logger.LogError("IMBALANCED TRANSACTION: TxId={TxId} Sum={Sum}", bad.TxId, bad.Sum);
        }
    }
}