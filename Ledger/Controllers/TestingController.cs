using Ledger.Banking;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestingController : ControllerBase
{
    private readonly IMockBank _bank;

    public TestingController(IMockBank bank) => _bank = bank;

    public record InjectDepositRequest(
        string CustomerIban,
        string CustomerName,
        decimal Amount,
        string? Reference);

    /// <summary>
    /// Simulates an incoming SEPA from a customer's private account to our pooling IBAN.
    /// The polling job will later pick it up and book it as a deposit.
    /// </summary>
    [HttpPost("inject-deposit")]
    public IActionResult InjectDeposit([FromBody] InjectDepositRequest request)
    {
        if (request.Amount <= 0) return BadRequest("Amount must be positive.");
        _bank.InjectDeposit(request.CustomerIban, request.CustomerName, request.Amount, request.Reference);
        return Ok();
    }
}