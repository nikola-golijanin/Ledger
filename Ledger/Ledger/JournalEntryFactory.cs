using Ledger.Domain;

namespace Ledger.Ledger;

public static class JournalEntryFactory
{
    public const short Debit = 1;
    public const short Credit = -1;

    public static JournalEntry Entry(Guid txId, int accountNumber, decimal amount, short direction, Guid? customerId = null)
        => new()
        {
            TransactionId = txId,
            AccountNumber = accountNumber,
            Amount = amount,
            Direction = direction,
            CustomerId = customerId,
            PostedAt = DateTime.UtcNow
        };

    /// <summary>
    /// Withdrawal PENDING → PROCESSING: reduce customer balance, park in suspense.
    ///   DR customer.viban         (liability down = debit)
    ///   CR suspense.withdrawal    (liability up = credit)
    /// </summary>
    public static IEnumerable<JournalEntry> WithdrawalToProcessing(Transaction tx)
    {
        yield return Entry(tx.Id, AccountNumbers.CustomerViban,      tx.Amount, Debit,  tx.CustomerId);
        yield return Entry(tx.Id, AccountNumbers.SuspenseWithdrawal, tx.Amount, Credit);
    }

    /// <summary>
    /// Withdrawal PROCESSING → SETTLED: clear suspense, money leaves pooling.
    ///   DR suspense.withdrawal    (liability down = debit)
    ///   CR bank.pooling           (asset down = credit)
    /// </summary>
    public static IEnumerable<JournalEntry> WithdrawalToSettled(Transaction tx)
    {
        yield return Entry(tx.Id, AccountNumbers.SuspenseWithdrawal, tx.Amount, Debit);
        yield return Entry(tx.Id, AccountNumbers.BankPooling,        tx.Amount, Credit);
    }

    /// <summary>
    /// Deposit observed in statement → SETTLED directly (money already arrived).
    ///   DR bank.pooling           (asset up = debit)
    ///   CR customer.viban         (liability up = credit)
    /// </summary>
    public static IEnumerable<JournalEntry> DepositSettled(Transaction tx)
    {
        yield return Entry(tx.Id, AccountNumbers.BankPooling,   tx.Amount, Debit);
        yield return Entry(tx.Id, AccountNumbers.CustomerViban, tx.Amount, Credit, tx.CustomerId);
    }
}