namespace Ledger.Domain;

public class PostingRule
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = default!;
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveUntil { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<PostingRuleLine> Lines { get; set; } = new List<PostingRuleLine>();
}