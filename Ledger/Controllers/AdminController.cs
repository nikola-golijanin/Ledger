using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly LedgerDbContext _db;
    private readonly PostingRuleValidator _validator;

    public AdminController(LedgerDbContext db, PostingRuleValidator validator)
    {
        _db = db;
        _validator = validator;
    }

 public record CreateNewRuleVersionRequest(
        string EventType,
        string? Description,
        IReadOnlyList<RuleLineDto> Lines);

    public record RuleLineDto(
        int AccountNumber,
        short Direction,
        bool CarriesCustomerId);

    public record CreateNewRuleVersionResponse(
        Guid Id,
        string EventType,
        int Version);

    [HttpPost]
    public async Task<IActionResult> PostNewVersion(
        [FromBody] CreateNewRuleVersionRequest request,
        CancellationToken ct)
    {
        // 1. Validate the proposed rule before touching anything
        var lineSpecs = request.Lines
            .Select(l => new RuleLineSpec(l.AccountNumber, l.Direction, l.CarriesCustomerId))
            .ToList();

        var validation = await _validator.ValidateAsync(request.EventType, lineSpecs, ct);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors });

        // 2. Look up the currently active rule for this event type
        var activeRule = await _db.PostingRules
            .FirstOrDefaultAsync(r => r.EventType == request.EventType && r.IsActive, ct);

        if (activeRule is null)
            return NotFound(new { error = $"No active rule for event type '{request.EventType}'. " +
                                          $"First rule is created via seeding, not this endpoint." });

        // 3. Atomic version bump
        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;

        activeRule.IsActive = false;
        activeRule.EffectiveUntil = now;

        var newRule = new PostingRule
        {
            Id = Guid.NewGuid(),
            EventType = request.EventType,
            Version = activeRule.Version + 1,
            IsActive = true,
            EffectiveFrom = now,
            EffectiveUntil = null,
            Description = request.Description,
            CreatedAt = now,
        };

        for (var i = 0; i < lineSpecs.Count; i++)
        {
            var l = lineSpecs[i];
            newRule.Lines.Add(new PostingRuleLine
            {
                Id = Guid.NewGuid(),
                PostingRuleId = newRule.Id,
                Sequence = i,
                AccountNumber = l.AccountNumber,
                Direction = l.Direction,
                CarriesCustomerId = l.CarriesCustomerId,
            });
        }

        _db.PostingRules.Add(newRule);

        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        return Ok(new CreateNewRuleVersionResponse(newRule.Id, newRule.EventType, newRule.Version));
    }
}