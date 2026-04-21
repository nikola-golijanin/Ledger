using Ledger.Domain;
using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Jobs;

public class StuckWithdrawalDetectorJob : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceProvider _services;
    private readonly ILogger<StuckWithdrawalDetectorJob> _logger;

    public StuckWithdrawalDetectorJob(
        IServiceProvider services,
        ILogger<StuckWithdrawalDetectorJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckOnce(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Stuck withdrawal check failed"); }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckOnce(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        var now = DateTime.UtcNow;

        var processingWithdrawals = await db.Transactions
            .Where(t => t.Type == TransactionType.Withdrawal
                        && t.Status == TransactionStatus.Processing)
            .Select(t => new { t.Id, t.CustomerId, t.Amount, t.SepaType, t.CreatedAt })
            .ToListAsync(ct);

        foreach (var w in processingWithdrawals)
        {
            var age = now - w.CreatedAt;
            var sla = WithdrawalSla.For(w.SepaType);

            if (age > sla)
            {
                _logger.LogWarning(
                    "STUCK WITHDRAWAL: TxId={TxId} CustomerId={CustomerId} Amount={Amount} SepaType={SepaType} Age={Age} SLA={Sla}",
                    w.Id, w.CustomerId, w.Amount, w.SepaType, age, sla);
            }
        }
    }
}