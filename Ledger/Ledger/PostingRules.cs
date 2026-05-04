using Ledger.Domain;

namespace Ledger.Ledger;

/// <summary>
/// Each event has a fixed set of entry templates. The engine reads these
/// and produces journal entries from them when an event is raised.
///
/// CarriesCustomerId = true means: copy Transaction.CustomerId onto this entry.
/// Used for entries that touch customer.viban — those should be attributable
/// to a specific customer for per-customer queries.
/// </summary>
public static class PostingRules
{
    public record EntryTemplate(int AccountNumber, short Direction, bool CarriesCustomerId);

    public const short Debit = 1;
    public const short Credit = -1;

   public static readonly IReadOnlyDictionary<string, IReadOnlyList<EntryTemplate>> Rules =
        new Dictionary<string, IReadOnlyList<EntryTemplate>>
        {
            [EventTypes.DepositDetectedCleanMatch] =
            [
                new EntryTemplate(AccountNumbers.BankPooling,   Debit,  CarriesCustomerId: false),
                new EntryTemplate(AccountNumbers.CustomerViban, Credit, CarriesCustomerId: true)
            ],

            [EventTypes.DepositDetectedRequiresReview] =
            [
                new EntryTemplate(AccountNumbers.BankPooling,           Debit,  CarriesCustomerId: false),
                new EntryTemplate(AccountNumbers.SuspenseDepositReview, Credit, CarriesCustomerId: false)
            ],

            [EventTypes.DepositReviewApproved] =
            [
                new EntryTemplate(AccountNumbers.SuspenseDepositReview, Debit,  CarriesCustomerId: false),
                new EntryTemplate(AccountNumbers.CustomerViban,         Credit, CarriesCustomerId: true)
            ],

            [EventTypes.WithdrawalInitiated] =
            [
                new EntryTemplate(AccountNumbers.CustomerViban,      Debit,  CarriesCustomerId: true),
                new EntryTemplate(AccountNumbers.SuspenseWithdrawal, Credit, CarriesCustomerId: false)
            ],

            [EventTypes.WithdrawalSettled] =
            [
                new EntryTemplate(AccountNumbers.SuspenseWithdrawal, Debit,  CarriesCustomerId: false),
                new EntryTemplate(AccountNumbers.BankPooling,        Credit, CarriesCustomerId: false)
            ],

            [EventTypes.BounceInitiated] =
            [
                new EntryTemplate(AccountNumbers.SuspenseDepositReview, Debit,  CarriesCustomerId: false),
                new EntryTemplate(AccountNumbers.SuspenseBounce,        Credit, CarriesCustomerId: false)
            ],

            [EventTypes.BounceSettled] =
            [
                new EntryTemplate(AccountNumbers.SuspenseBounce, Debit,  CarriesCustomerId: false),
                new EntryTemplate(AccountNumbers.BankPooling,    Credit, CarriesCustomerId: false)
            ],
        };
   
   public static IReadOnlyList<EntryTemplate> For(string eventType) =>
       Rules.TryGetValue(eventType, out var rule)
           ? rule
           : throw new InvalidOperationException($"No posting rule for event type '{eventType}'");
}

