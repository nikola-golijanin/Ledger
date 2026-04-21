using Ledger.Banking;
using Ledger.Domain;
using Ledger.Infrastructure;
using Ledger.Ledger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WithdrawalsController : ControllerBase
{
    // Will move to appsettings later
    private const string PoolingIban = "DE00000000000000000000";
    private const string PoolingName = "Broker Pooling";

    private readonly LedgerDbContext _db;
    private readonly IMockBank _bank;

    public WithdrawalsController(LedgerDbContext db, IMockBank bank)
    {
        _db = db;
        _bank = bank;
    }

    public record CreateWithdrawalRequest(
        Guid CustomerId,
        string CustomerIban,
        string CustomerName,
        decimal Amount,
        SepaType SepaType);

    public record WithdrawalResponse(
        Guid TransactionId,
        TransactionStatus Status);

    [HttpPost]
    public async Task<ActionResult<WithdrawalResponse>> Create(
        [FromBody] CreateWithdrawalRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            return BadRequest("Amount must be positive.");


        await using var dbTx = await _db.Database.BeginTransactionAsync(ct);

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({request.CustomerId.ToString()}))", ct);

        var signedSum = await _db.JournalEntries
            .Where(e => e.AccountNumber == AccountNumbers.CustomerViban
                        && e.CustomerId == request.CustomerId)
            .SumAsync(e => e.Direction * e.Amount, ct);

        var available = -signedSum;

        if (available < request.Amount)
            return BadRequest(new { error = "Insufficient funds", available });


        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Type = TransactionType.Withdrawal,
            Status = TransactionStatus.Processing,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            Currency = "EUR",
            ExternalRef = null, // set below
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        tx.ExternalRef = tx.Id.ToString(); // reference echoed by bank

        foreach (var entry in JournalEntryFactory.WithdrawalToProcessing(tx))
            tx.Entries.Add(entry);

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);
        await dbTx.CommitAsync(ct);

        // Hand off to the bank. In real life we'd want an outbox here so the DB write
        // and the external call can't diverge — parking that for later.
        _bank.SubmitWithdrawal(request.CustomerIban, request.CustomerName, request.Amount, tx.ExternalRef);

        return Ok(new WithdrawalResponse(tx.Id, tx.Status));
    }

    [HttpGet("{customerId:guid}")]
    public async Task<IActionResult> GetAsync(Guid customerId, CancellationToken ct)
    {
        var signed = await _db.JournalEntries
            .Where(e => e.AccountNumber == AccountNumbers.CustomerViban
                        && e.CustomerId == customerId)
            .SumAsync(e => e.Direction * e.Amount, ct);
        return Ok(-signed);
    }


    public record CreateFaultyWithdrawalRequest(
        Guid CustomerId,
        decimal Amount,
        SepaType SepaType);

    /// <summary>
    /// Simulates a crash between DB commit and bank submission.
    /// Journal entries are booked, but the bank is never told.
    /// The transaction will sit in Processing forever until the reconciliation job flags it.
    /// </summary>
    [HttpPost("faulty")]
    public async Task<ActionResult<WithdrawalResponse>> CreateFaulty(
        [FromBody] CreateFaultyWithdrawalRequest request,
        CancellationToken ct)
    {
        if (request.Amount <= 0)
            return BadRequest("Amount must be positive.");

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
            UpdatedAt = DateTime.UtcNow
        };
        tx.ExternalRef = tx.Id.ToString();

        foreach (var entry in JournalEntryFactory.WithdrawalToProcessing(tx))
            tx.Entries.Add(entry);

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync(ct);

        // DELIBERATELY NOT calling _bank.SubmitWithdrawal — simulates the crash
        return Ok(new WithdrawalResponse(tx.Id, tx.Status));
    }
}