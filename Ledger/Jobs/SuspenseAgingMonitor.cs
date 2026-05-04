using Ledger.Domain;
using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Jobs;

public class SuspenseAgingMonitor : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private static readonly Dictionary<int, TimeSpan> SlaByAccount = new()
    {
        [AccountNumbers.SuspenseWithdrawal]    = TimeSpan.FromSeconds(30),
        [AccountNumbers.SuspenseDepositReview] = TimeSpan.FromHours(4),
        [AccountNumbers.SuspenseBounce]        = TimeSpan.FromMinutes(5),
    };

    private readonly IServiceProvider _services;
    private readonly ILogger<SuspenseAgingMonitor> _logger;

    public SuspenseAgingMonitor(IServiceProvider services, ILogger<SuspenseAgingMonitor> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckOnce(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Suspense aging check failed"); }
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckOnce(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var now = DateTime.UtcNow;

        foreach (var (accountNumber, sla) in SlaByAccount)
        {
            var cutoff = now - sla;
            var stuck = await db.JournalEntries
                .Where(e => e.AccountNumber == accountNumber)
                .GroupBy(e => e.TransactionId)
                .Select(g => new
                {
                    TxId = g.Key,
                    Net = g.Sum(e => (decimal)e.Direction * e.Amount),
                    Oldest = g.Min(e => e.PostedAt)
                })
                .Where(x => x.Net != 0m && x.Oldest < cutoff)
                .ToListAsync(ct);

            foreach (var s in stuck)
                _logger.LogWarning("STUCK IN SUSPENSE: Account={Account} TxId={TxId} Net={Net} Age={Age} SLA={Sla}",
                    accountNumber, s.TxId, s.Net, now - s.Oldest, sla);
        }
    }
}