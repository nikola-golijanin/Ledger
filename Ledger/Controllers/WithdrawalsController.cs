using Ledger.Banking;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Controllers;

[ApiController]
[Route("api/withdrawals")]
public class WithdrawalsController : ControllerBase
{
    private readonly LedgerDbContext _db;
    private readonly IMockBank _bank;
    private readonly IPostingEngine _posting;

    public WithdrawalsController(LedgerDbContext db, IMockBank bank, IPostingEngine posting)
    {
        _db = db;
        _bank = bank;
        _posting = posting;
    }

    public record CreateWithdrawalRequest(
        Guid CustomerId,
        string CustomerIban,
        string CustomerName,
        decimal Amount,
        SepaType SepaType);

    public record WithdrawalResponse(Guid TransactionId, TransactionStatus Status);

    [HttpPost]
    public async Task<ActionResult<WithdrawalResponse>> Create([FromBody] CreateWithdrawalRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("Amount must be positive.");

        var tx = NewWithdrawal(request);
        _db.Transactions.Add(tx);

        await _posting.RaiseEventAsync(tx, EventTypes.WithdrawalInitiated,
            new { request.CustomerIban, request.CustomerName }, ct);

        await _db.SaveChangesAsync(ct);

        _bank.SubmitWithdrawal(request.CustomerIban, request.CustomerName, request.Amount, tx.Id.ToString());
        return Ok(new WithdrawalResponse(tx.Id, tx.Status));
    }

    

    private static Transaction NewWithdrawal(CreateWithdrawalRequest req)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = TransactionType.Withdrawal,
            Status = TransactionStatus.Processing,
            CustomerId = req.CustomerId,
            Amount = req.Amount,
            Currency = "EUR",
            SepaType = req.SepaType,
            CounterpartyIban = req.CustomerIban,
            CounterpartyName = req.CustomerName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        tx.ExternalRef = tx.Id.ToString();
        return tx;
    }
}