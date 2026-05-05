namespace Ledger.Domain;

public class PostingRuleLine
{
    public Guid Id { get; set; }
    public Guid PostingRuleId { get; set; }
    public int Sequence { get; set; }
    public int AccountNumber { get; set; }
    public short Direction { get; set; }
    public bool CarriesCustomerId { get; set; }

    public PostingRule PostingRule { get; set; } = default!;
    public Account Account { get; set; } = default!;
}