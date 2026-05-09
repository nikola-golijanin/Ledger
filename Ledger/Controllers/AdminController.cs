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
    private readonly PostingRuleValidator _ruleValidator;
    
    private readonly CorrectionValidator _correctionValidator;
    private readonly ICorrectionService _correctionService;

    public AdminController(LedgerDbContext db,
        PostingRuleValidator ruleValidator,
        CorrectionValidator correctionValidator,
        ICorrectionService correctionService)
    {
        _db = db;
        _ruleValidator = ruleValidator;
        _correctionValidator = correctionValidator;
        _correctionService = correctionService;
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

    [HttpPost("posting-rules")]
    public async Task<IActionResult> PostNewVersion(
        [FromBody] CreateNewRuleVersionRequest request,
        CancellationToken ct)
    {
        // 1. Validate the proposed rule before touching anything
        var lineSpecs = request.Lines
            .Select(l => new RuleLineSpec(l.AccountNumber, l.Direction, l.CarriesCustomerId))
            .ToList();

        var validation = await _ruleValidator.ValidateAsync(request.EventType, lineSpecs, ct);
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
    
    
    public record PostCorrectionApiRequest(
        string Reason,
        string Description,
        string RequestedBy,
        string Currency,
        Guid? CorrectsTransactionId,
        string? IdempotencyKey,
        IReadOnlyList<CorrectionLineDto> Lines);

    public record CorrectionLineDto(
        int AccountNumber,
        short Direction,
        decimal Amount,
        Guid? CustomerId);
    
    [HttpPost("corrections")]
    public async Task<IActionResult> Post(
        [FromBody] PostCorrectionApiRequest request,
        CancellationToken ct)
    {
        var lineSpecs = request.Lines?
            .Select(l => new CorrectionLineSpec(l.AccountNumber, l.Direction, l.Amount, l.CustomerId))
            .ToList();

        var validation = await _correctionValidator.ValidateAsync(
            request.Reason,
            request.Description,
            request.RequestedBy,
            request.Currency,
            request.CorrectsTransactionId,
            lineSpecs,
            ct);

        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors });

        var result = await _correctionService.PostAsync(new PostCorrectionRequest(
            request.Reason,
            request.Description,
            request.RequestedBy,
            request.Currency,
            request.CorrectsTransactionId,
            request.IdempotencyKey,
            lineSpecs!), ct);

        return Ok(result);
    }
    
    [HttpGet("posting-rules/diff/{eventType}")]
    public async Task<IActionResult> DiffPostingRule(
        string eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromServices] IPostingRuleDiffService diffService,
        CancellationToken ct)
    {
        try
        {
            var report = await diffService.DiffAsync(eventType, from, to, ct);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}