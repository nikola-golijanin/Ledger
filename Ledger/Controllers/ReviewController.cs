using Ledger.Banking;
using Ledger.Customers;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewController : ControllerBase
{
    private readonly LedgerDbContext _db;
    private readonly IMockBank _bank;
    private readonly ICustomerRegistry _customers;
    private readonly IPostingEngine _posting;

    public ReviewController(LedgerDbContext db, IMockBank bank, ICustomerRegistry customers, IPostingEngine posting)
    {
        _db = db;
        _bank = bank;
        _customers = customers;
        _posting = posting;
    }

    public record ApproveRequest(Guid CustomerId, string ReviewedBy);
    public record RejectRequest(string ReviewedBy);

    [HttpPost("{txId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid txId, [FromBody] ApproveRequest request, CancellationToken ct)
    {
        var tx = await _db.Transactions
            .Include(t => t.Entries)
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null) return NotFound();
        if (tx.Type != TransactionType.Deposit || tx.ReviewReason is null || tx.Status != TransactionStatus.Processing)
            return BadRequest("Transaction is not a deposit awaiting review.");

        if (_customers.FindById(request.CustomerId) is null)
            return BadRequest("Unknown customer.");

        tx.CustomerId = request.CustomerId;
        await _posting.RaiseEventAsync(tx, EventTypes.DepositReviewApproved, new { request.ReviewedBy }, ct);

        tx.Status = TransactionStatus.Settled;
        tx.ReviewedAt = DateTime.UtcNow;
        tx.ReviewedBy = request.ReviewedBy;
        tx.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { tx.Id, tx.Status, tx.CustomerId });
    }

    [HttpPost("{txId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid txId, [FromBody] RejectRequest request, CancellationToken ct)
    {
        var tx = await _db.Transactions
            .Include(t => t.Entries).Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null) return NotFound();
        if (tx.Type != TransactionType.Deposit || tx.ReviewReason is null || tx.Status != TransactionStatus.Processing)
            return BadRequest("Transaction is not a deposit awaiting review.");

        await _posting.RaiseEventAsync(tx, EventTypes.BounceInitiated, new { request.ReviewedBy }, ct);

        tx.ReviewedAt = DateTime.UtcNow;
        tx.ReviewedBy = request.ReviewedBy;
        tx.UpdatedAt = DateTime.UtcNow;
        // status stays Processing — bounce hasn't settled yet

        await _db.SaveChangesAsync(ct);

        _bank.SubmitBounce(tx.CounterpartyIban!, tx.CounterpartyName!, tx.Amount, tx.Id.ToString());
        return Ok(new { tx.Id, tx.Status, awaitingBounce = true });
    }
}