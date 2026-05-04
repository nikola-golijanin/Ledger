namespace Ledger.Domain;

public enum ReviewReason
{
    NameMismatch = 1,
    IbanNotOnFile = 2,
    SanctionsHit = 3,
    AmountExceedsThreshold = 4
}