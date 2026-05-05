namespace Ledger.Domain;

public class PostingRule
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = default!;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<PostingRuleLine> Lines { get; set; } = new List<PostingRuleLine>();
}