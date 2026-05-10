using Ledger.Banking;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Controllers;

[ApiController]
[Route("api/testing")]
public class TestingController : ControllerBase
{
    private readonly IMockBank _bank;
    private readonly LedgerDbContext _db;
    private IPostingEngine _posting;

    public TestingController(IMockBank bank, LedgerDbContext db, IPostingEngine posting)
    {
        _bank = bank;
        _db = db;
        _posting = posting;
    }

    public record InjectDepositRequest(string CustomerIban, string CustomerName, decimal Amount, string? Reference);

    [HttpPost("inject-deposit")]
    public IActionResult InjectDeposit([FromBody] InjectDepositRequest request)
    {
        if (request.Amount <= 0) return BadRequest("Amount must be positive.");
        _bank.InjectDeposit(request.CustomerIban, request.CustomerName, request.Amount, request.Reference);
        return Ok();
    }

    public record InjectDepositForReviewRequest(string CounterpartyIban, string CounterpartyName, decimal Amount, ReviewReason Reason);

    [HttpPost("inject-deposit-for-review")]
    public IActionResult InjectDepositForReview([FromBody] InjectDepositForReviewRequest request)
    {
        if (request.Amount <= 0) return BadRequest("Amount must be positive.");
        _bank.InjectDeposit(request.CounterpartyIban, request.CounterpartyName, request.Amount, reference: null, forceReview: request.Reason);
        return Ok();
    }
    
    public record CreateFaultyWithdrawalRequest(Guid CustomerId, decimal Amount, SepaType SepaType);

    [HttpPost("faulty-withdrawal")]
    public async Task<ActionResult<WithdrawalsController.WithdrawalResponse>> CreateFaulty([FromBody] CreateFaultyWithdrawalRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("Amount must be positive.");

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = TransactionType.Withdrawal,
            Status = TransactionStatus.Processing,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            Currency = "EUR",
            SepaType = request.SepaType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        tx.ExternalRef = tx.Id.ToString();

        _db.Transactions.Add(tx);
        await _posting.RaiseEventAsync(tx, EventTypes.WithdrawalInitiated, new { faulty = true }, ct);
        await _db.SaveChangesAsync(ct);

        // DELIBERATELY no SubmitWithdrawal — simulates a crash
        return Ok(new WithdrawalsController.WithdrawalResponse(tx.Id, tx.Status));
    }
    
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        // Order matters because of FK constraints:
        // journal_entries → accounting_events → transactions
        await _db.JournalEntries.ExecuteDeleteAsync(ct);
        await _db.AccountingEvents.ExecuteDeleteAsync(ct);
        await _db.Transactions.ExecuteDeleteAsync(ct);
        _bank.Clear();

        return Ok(new { message = "Ledger data cleared. Chart of accounts and posting rules preserved." });
    }
    
}