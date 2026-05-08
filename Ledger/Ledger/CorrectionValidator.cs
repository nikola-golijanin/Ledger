using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Ledger;

public record CorrectionLineSpec(
    int AccountNumber,
    short Direction,
    decimal Amount,
    Guid? CustomerId);

public record CorrectionValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public class CorrectionValidator
{
    private readonly LedgerDbContext _db;

    public CorrectionValidator(LedgerDbContext db) => _db = db;

    public async Task<CorrectionValidationResult> ValidateAsync(
        string? reason,
        string? description,
        string? requestedBy,
        string currency,
        Guid? correctsTransactionId,
        IReadOnlyList<CorrectionLineSpec>? lines,
        CancellationToken ct)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(reason))
            errors.Add("reason is required.");
        if (string.IsNullOrWhiteSpace(description))
            errors.Add("description is required.");
        if (string.IsNullOrWhiteSpace(requestedBy))
            errors.Add("requestedBy is required.");
        if (!string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Only EUR is supported, got '{currency}'.");

        if (lines is null || lines.Count < 2)
        {
            errors.Add("A correction must have at least 2 lines.");
            return new CorrectionValidationResult(false, errors);
        }

        // Per-line checks
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Direction != 1 && l.Direction != -1)
                errors.Add($"Line {i}: direction must be +1 or -1, got {l.Direction}.");
            if (l.Amount <= 0m)
                errors.Add($"Line {i}: amount must be positive, got {l.Amount}.");
        }

        // Balance check: sum of (direction * amount) must be exactly zero
        var signedSum = lines.Sum(l => l.Direction * l.Amount);
        if (signedSum != 0m)
            errors.Add($"Lines do not balance: signed sum is {signedSum}, must be 0.");

        // Account validity: exist and postable
        var accountNumbers = lines.Select(l => l.AccountNumber).Distinct().ToArray();
        var accounts = await _db.Accounts
            .Where(a => accountNumbers.Contains(a.Number))
            .ToDictionaryAsync(a => a.Number, ct);

        foreach (var n in accountNumbers)
        {
            if (!accounts.TryGetValue(n, out var account))
                errors.Add($"Account {n} does not exist.");
            else if (!account.IsPostable)
                errors.Add($"Account {n} ({account.Code}) is not postable (it's a rollup).");
        }

        // CorrectsTransactionId reference check
        if (correctsTransactionId is { } refId)
        {
            var exists = await _db.Transactions.AnyAsync(t => t.Id == refId, ct);
            if (!exists)
                errors.Add($"correctsTransactionId {refId} does not match any existing transaction.");
        }

        return new CorrectionValidationResult(errors.Count == 0, errors);
    }
}