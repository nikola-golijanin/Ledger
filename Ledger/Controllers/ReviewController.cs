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

    public ReviewController(LedgerDbContext db, IMockBank bank, ICustomerRegistry customers)
    {
        _db = db;
        _bank = bank;
        _customers = customers;
    }


    [HttpPost("{txId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid txId, [FromBody] ApproveRequest request, CancellationToken ct)
    {
        var tx = await _db.Transactions
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null) return NotFound();
        if (tx.Type != TransactionType.Deposit || tx.ReviewReason is null || tx.Status != TransactionStatus.Processing)
            return BadRequest(new { error = "Transaction is not a deposit awaiting review." });

        if (_customers.FindById(request.CustomerId) is null)
            return BadRequest(new { error = "Unknown customer." });

        tx.CustomerId = request.CustomerId;
        foreach (var e in JournalEntryFactory.ReviewApproved(tx)) tx.Entries.Add(e);
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
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == txId, ct);

        if (tx is null) return NotFound();
        if (tx.Type != TransactionType.Deposit || tx.ReviewReason is null || tx.Status != TransactionStatus.Processing)
            return BadRequest(new { error = "Transaction is not a deposit awaiting review." });

        // Move from review bucket to bounce bucket, then instruct bank to send the money back
        foreach (var e in JournalEntryFactory.ReviewRejected(tx)) tx.Entries.Add(e);
        tx.ReviewedAt = DateTime.UtcNow;
        tx.ReviewedBy = request.ReviewedBy;
        tx.UpdatedAt = DateTime.UtcNow;
        // status stays Processing — bounce hasn't settled yet

        await _db.SaveChangesAsync(ct);

        _bank.SubmitBounce(tx.CounterpartyIban!, tx.CounterpartyName!, tx.Amount, tx.Id.ToString());
        return Ok(new { tx.Id, tx.Status, awaitingBounce = true });
    }

    public record ApproveRequest(Guid CustomerId, string ReviewedBy);

    public record RejectRequest(string ReviewedBy);
}