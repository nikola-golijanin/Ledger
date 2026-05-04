using Ledger.Banking;
using Ledger.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Controllers;

[ApiController]
[Route("api/testing")]
public class TestingController : ControllerBase
{
    private readonly IMockBank _bank;
    public TestingController(IMockBank bank) => _bank = bank;

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
}