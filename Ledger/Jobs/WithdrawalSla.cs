using Ledger.Domain;

namespace Ledger.Jobs;

public static class WithdrawalSla
{
    // How long a withdrawal can sit in Processing before we consider it stuck.
    public static TimeSpan For(SepaType? type) => type switch
    {
        SepaType.Instant  => TimeSpan.FromSeconds(30),
        SepaType.Standard => TimeSpan.FromHours(24),
        _                 => TimeSpan.FromHours(24)
    };
}