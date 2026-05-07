using Ledger.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Ledger;

public record RuleLineSpec(int AccountNumber, short Direction, bool CarriesCustomerId);

public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, []);
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}

public class PostingRuleValidator
{
    private readonly LedgerDbContext _db;

    public PostingRuleValidator(LedgerDbContext db) => _db = db;

    public async Task<ValidationResult> ValidateAsync(
        string eventType,
        IReadOnlyList<RuleLineSpec> lines,
        CancellationToken ct)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(eventType))
            errors.Add("EventType is required.");

        if (lines is null || lines.Count < 2)
            errors.Add("A rule must have at least 2 lines.");

        if (lines is null)
            return errors.Count == 0
                ? ValidationResult.Ok()
                : new ValidationResult(false, errors);
        
        // Every direction must be +1 or -1
        foreach (var (line, idx) in lines.Select((l, i) => (l, i)))
        {
            if (line.Direction != 1 && line.Direction != -1)
                errors.Add($"Line {idx}: direction must be +1 or -1, got {line.Direction}.");
        }

        // Sum of directions must be zero (rule must balance for any uniform amount)
        var directionSum = lines.Sum(l => l.Direction);
        if (directionSum != 0)
            errors.Add($"Rule does not balance: sum of directions is {directionSum}, must be 0.");

        // All accounts must exist and be postable
        var accountNumbers = lines.Select(l => l.AccountNumber).Distinct().ToArray();
        var accounts = await _db.Accounts
            .ToDictionaryAsync(a => a.Number, ct);

        foreach (var n in accountNumbers)
        {
            if (!accounts.TryGetValue(n, out var account))
                errors.Add($"Account {n} does not exist.");
            else if (!account.IsPostable)
                errors.Add($"Account {n} ({account.Code}) is not postable (it's a rollup).");
        }

        return errors.Count == 0
            ? ValidationResult.Ok()
            : new ValidationResult(false, errors);
    }
}