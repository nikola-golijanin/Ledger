using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure;

public static class PostingRulesSeeder
{
    private const short Debit = 1;
    private const short Credit = -1;

    private record Line(int AccountNumber, short Direction, bool CarriesCustomerId);
    private record Seed(string EventType, string Description, Line[] Lines);

    private static readonly Seed[] Seeds =
    [
        new(
            EventTypes.DepositDetectedCleanMatch,
            "Money arrived at pooling, IBAN matches a known customer.",
            [
                new(AccountNumbers.BankPooling,   Debit,  CarriesCustomerId: false),
                new(AccountNumbers.CustomerViban, Credit, CarriesCustomerId: true),
            ]),

        new(
            EventTypes.DepositDetectedRequiresReview,
            "Money arrived at pooling, identity uncertain, parked in review.",
            [
                new(AccountNumbers.BankPooling,           Debit,  CarriesCustomerId: false),
                new(AccountNumbers.SuspenseDepositReview, Credit, CarriesCustomerId: false),
            ]),

        new(
            EventTypes.DepositReviewApproved,
            "Ops approved a review, money released to customer's vIBAN.",
            [
                new(AccountNumbers.SuspenseDepositReview, Debit,  CarriesCustomerId: false),
                new(AccountNumbers.CustomerViban,         Credit, CarriesCustomerId: true),
            ]),

        new(
            EventTypes.WithdrawalInitiated,
            "Customer requested withdrawal, customer balance debited, parked in suspense.",
            [
                new(AccountNumbers.CustomerViban,      Debit,  CarriesCustomerId: true),
                new(AccountNumbers.SuspenseWithdrawal, Credit, CarriesCustomerId: false),
            ]),

        new(
            EventTypes.WithdrawalSettled,
            "Bank confirmed withdrawal sent, suspense cleared, pooling debited.",
            [
                new(AccountNumbers.SuspenseWithdrawal, Debit,  CarriesCustomerId: false),
                new(AccountNumbers.BankPooling,        Credit, CarriesCustomerId: false),
            ]),

        new(
            EventTypes.BounceInitiated,
            "Ops rejected a review, money moved from review to bounce suspense.",
            [
                new(AccountNumbers.SuspenseDepositReview, Debit,  CarriesCustomerId: false),
                new(AccountNumbers.SuspenseBounce,        Credit, CarriesCustomerId: false),
            ]),

        new(
            EventTypes.BounceSettled,
            "Bank confirmed refund sent to original sender, bounce cleared, pooling debited.",
            [
                new(AccountNumbers.SuspenseBounce, Debit,  CarriesCustomerId: false),
                new(AccountNumbers.BankPooling,    Credit, CarriesCustomerId: false),
            ]),
    ];

    /// <summary>
    /// Seeds v1 active rules for any event type that has no rule yet.
    /// Does NOT modify existing rules — new versions go through admin endpoints (later).
    /// </summary>
    public static async Task SeedAsync(LedgerDbContext db, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        foreach (var seed in Seeds)
        {
            var anyExisting = await db.PostingRules
                .AnyAsync(r => r.EventType == seed.EventType, ct);

            if (anyExisting) continue;

            var rule = new PostingRule
            {
                Id = Guid.NewGuid(),
                EventType = seed.EventType,
                Version = 1,
                IsActive = true,
                EffectiveFrom = now,
                EffectiveUntil = null,
                Description = seed.Description,
                CreatedAt = now,
            };

            for (var i = 0; i < seed.Lines.Length; i++)
            {
                var line = seed.Lines[i];
                rule.Lines.Add(new PostingRuleLine
                {
                    Id = Guid.NewGuid(),
                    PostingRuleId = rule.Id,
                    Sequence = i,
                    AccountNumber = line.AccountNumber,
                    Direction = line.Direction,
                    CarriesCustomerId = line.CarriesCustomerId,
                });
            }

            db.PostingRules.Add(rule);
        }

        await db.SaveChangesAsync(ct);
    }
}