namespace Ledger.Domain;

public enum TransactionStatus
{
    Pending = 1,
    Processing = 2,
    Settled = 3,
    Failed = 4,
    Reversed = 5
}