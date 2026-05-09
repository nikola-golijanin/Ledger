using Ledger.Domain;

namespace Ledger.Ledger;

public record ExpectedEntry(
    int AccountNumber,
    short Direction,
    decimal Amount,
    Guid? CustomerId);

public static class ExpectedEntriesCalculator
{
    /// <summary>
    /// Pure function: given a rule and the transaction the event belongs to,
    /// returns what the entries would have looked like if this rule produced them.
    /// Does not touch the DB.
    /// </summary>
    public static IReadOnlyList<ExpectedEntry> Compute(PostingRule rule, Transaction tx)
    {
        return rule.Lines
            .OrderBy(l => l.Sequence)
            .Select(l => new ExpectedEntry(
                AccountNumber: l.AccountNumber,
                Direction: l.Direction,
                Amount: tx.Amount,
                CustomerId: l.CarriesCustomerId ? tx.CustomerId : null))
            .ToList();
    }
}