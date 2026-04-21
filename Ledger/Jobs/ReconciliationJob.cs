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
            try
            {
                await CheckOnce(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckOnce(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        var balances = await db.JournalEntries
            .GroupBy(e => e.AccountNumber)
            .Select(g => new { Account = g.Key, Signed = g.Sum(e => (decimal)e.Direction * e.Amount) })
            .ToDictionaryAsync(x => x.Account, x => x.Signed, ct);

        decimal Get(int acc) => balances.TryGetValue(acc, out var v) ? v : 0m;

        var pooling = Get(AccountNumbers.BankPooling);
        var customerSigned = Get(AccountNumbers.CustomerViban);
        var suspenseDepositInf = Get(AccountNumbers.SuspenseDepositInflight);
        var suspenseWithdrawal = Get(AccountNumbers.SuspenseWithdrawal);
        var suspenseReview = Get(AccountNumbers.SuspenseDepositReview);
        var suspenseBounce = Get(AccountNumbers.SuspenseBounce);

        // Flip liabilities to natural form for readability
        var customerOwed = -customerSigned;
        var withdrawalInFlight = -suspenseWithdrawal;
        var inReview = -suspenseReview;
        var bounceInFlight = -suspenseBounce;

        // Invariant:
        //   pooling + suspense.deposit.inflight
        //     == customer.viban + suspense.withdrawal + suspense.deposit.review + suspense.bounce
        var drift = pooling
                    + suspenseDepositInf
                    - customerOwed
                    - withdrawalInFlight
                    - inReview
                    - bounceInFlight;

        if (drift != 0m)
        {
            _logger.LogError(
                "RECONCILIATION DRIFT: {Drift} | pooling={P} customerOwed={C} withdrawal={W} review={R} bounce={B} inflight={I}",
                drift, pooling, customerOwed, withdrawalInFlight, inReview, bounceInFlight, suspenseDepositInf);
        }
        else
        {
            _logger.LogInformation(
                "Reconciliation OK | pooling={P} customerOwed={C} withdrawal={W} review={R} bounce={B}",
                pooling, customerOwed, withdrawalInFlight, inReview, bounceInFlight);
        }

        var imbalancedTx = await db.JournalEntries
            .GroupBy(e => e.TransactionId)
            .Select(g => new { TxId = g.Key, Sum = g.Sum(e => (decimal)e.Direction * e.Amount) })
            .Where(x => x.Sum != 0m)
            .ToListAsync(ct);

        foreach (var bad in imbalancedTx)
            _logger.LogError("IMBALANCED TRANSACTION: TxId={TxId} Sum={Sum}", bad.TxId, bad.Sum);
    }
}