using Ledger.Domain;

namespace Ledger.Ledger;

public static class JournalEntryFactory
{
    public const short Debit = 1;
    public const short Credit = -1;

    private static JournalEntry Entry(Guid txId, int accountNumber, decimal amount, short direction, Guid? customerId = null)
        => new()
        {
            TransactionId = txId,
            AccountNumber = accountNumber,
            Amount = amount,
            Direction = direction,
            CustomerId = customerId,
            PostedAt = DateTime.UtcNow
        };

    // ── WITHDRAWAL ────────────────────────────────────────────────
    public static IEnumerable<JournalEntry> WithdrawalToProcessing(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.CustomerViban,      tx.Amount, Debit,  tx.CustomerId),
        Entry(tx.Id, AccountNumbers.SuspenseWithdrawal, tx.Amount, Credit)
    ];

    public static IEnumerable<JournalEntry> WithdrawalToSettled(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.SuspenseWithdrawal, tx.Amount, Debit),
        Entry(tx.Id, AccountNumbers.BankPooling,        tx.Amount, Credit)
    ];

    // ── DEPOSIT — CLEAN PATH ──────────────────────────────────────
    public static IEnumerable<JournalEntry> DepositSettled(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.BankPooling,   tx.Amount, Debit),
        Entry(tx.Id, AccountNumbers.CustomerViban, tx.Amount, Credit, tx.CustomerId)
    ];

    // ── DEPOSIT — REVIEW PATH ─────────────────────────────────────
    public static IEnumerable<JournalEntry> DepositToReview(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.BankPooling,          tx.Amount, Debit),
        Entry(tx.Id, AccountNumbers.SuspenseDepositReview, tx.Amount, Credit)
    ];

    public static IEnumerable<JournalEntry> ReviewApproved(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.SuspenseDepositReview, tx.Amount, Debit),
        Entry(tx.Id, AccountNumbers.CustomerViban,         tx.Amount, Credit, tx.CustomerId)
    ];

    public static IEnumerable<JournalEntry> ReviewRejected(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.SuspenseDepositReview, tx.Amount, Debit),
        Entry(tx.Id, AccountNumbers.SuspenseBounce,        tx.Amount, Credit)
    ];

    public static IEnumerable<JournalEntry> BounceSettled(Transaction tx) =>
    [
        Entry(tx.Id, AccountNumbers.SuspenseBounce, tx.Amount, Debit),
        Entry(tx.Id, AccountNumbers.BankPooling,    tx.Amount, Credit)
    ];
}