namespace Ledger.Domain;

/// <summary>
/// Constants for accounting event names. Always reference these.
/// </summary>
public static class EventTypes
{
    public const string DepositDetectedCleanMatch     = "deposit.detected.clean_match";
    public const string DepositDetectedRequiresReview = "deposit.detected.requires_review";
    public const string DepositReviewApproved         = "deposit.review.approved";
    public const string WithdrawalInitiated           = "withdrawal.initiated";
    public const string WithdrawalSettled             = "withdrawal.settled";
    public const string BounceInitiated               = "bounce.initiated";
    public const string BounceSettled                 = "bounce.settled";
}