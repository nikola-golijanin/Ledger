using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Jobs;

public class ReconciliationJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(IServiceProvider services, ILogger<ReconciliationJob> logger)
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

        // The accounting equation: SUM(direction * amount) across ALL entries == 0
        var drift = await db.JournalEntries
            .SumAsync(e => e.Direction * e.Amount, ct);

        if (drift != 0m)
            _logger.LogError("ACCOUNTING EQUATION VIOLATED. Drift={Drift}", drift);
        else
            _logger.LogInformation("Reconciliation OK (drift=0)");

        // Per-transaction balance check
        var imbalanced = await db.JournalEntries
            .GroupBy(e => e.TransactionId)
            .Select(g => new { TxId = g.Key, Sum = g.Sum(e => e.Direction * e.Amount) })
            .Where(x => x.Sum != 0m)
            .ToListAsync(ct);

        foreach (var bad in imbalanced)
            _logger.LogError("IMBALANCED TRANSACTION: {TxId} sum={Sum}", bad.TxId, bad.Sum);
    }
}