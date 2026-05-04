namespace Ledger.Domain;

public enum TransactionType
{
    Deposit = 1,
    Withdrawal = 2,
    TreasuryToMarket = 3,
    MarketToTreasury = 4
}