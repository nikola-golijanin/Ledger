namespace Ledger.Banking;

public class MockBank : IMockBank
{
    private readonly List<BankStatementEntry> _statement = new();
    private readonly object _lock = new();

    public void SubmitWithdrawal(string customerIban, string customerName, decimal amount, string reference)
    {
        lock (_lock)
        {
            _statement.Add(new BankStatementEntry
            {
                Direction = StatementDirection.Outgoing,
                Amount = amount,
                CounterpartyIban = customerIban,
                CounterpartyName = customerName,
                Reference = reference
            });
        }
    }

    public void InjectDeposit(string customerIban, string customerName, decimal amount, string? reference = null)
    {
        lock (_lock)
        {
            _statement.Add(new BankStatementEntry
            {
                Direction = StatementDirection.Incoming,
                Amount = amount,
                CounterpartyIban = customerIban,
                CounterpartyName = customerName,
                Reference = reference
            });
        }
    }

    public IReadOnlyList<BankStatementEntry> GetUnprocessed()
    {
        lock (_lock)
        {
            return _statement.Where(e => !e.Processed).ToList();
        }
    }

    public void MarkProcessed(Guid entryId)
    {
        lock (_lock)
        {
            var entry = _statement.FirstOrDefault(e => e.Id == entryId);
            if (entry is not null) entry.Processed = true;
        }
    }
}

public interface IMockBank
{
    /// <summary>
    /// Called by our WithdrawalsController after we've booked the transaction.
    /// The bank receives the instruction and records it on the statement.
    /// </summary>
    void SubmitWithdrawal(string customerIban, string customerName, decimal amount, string reference);

    /// <summary>
    /// Test helper — injects a fake incoming transfer, as if a customer sent a SEPA to us.
    /// </summary>
    void InjectDeposit(string customerIban, string customerName, decimal amount, string? reference = null);

    /// <summary>
    /// Returns unprocessed statement entries (what our polling job reads).
    /// </summary>
    IReadOnlyList<BankStatementEntry> GetUnprocessed();

    /// <summary>
    /// Marks an entry as processed so we won't ingest it again.
    /// </summary>
    void MarkProcessed(Guid entryId);
}

public enum StatementDirection
{
    Incoming = 1,   // money arrived at pooling (a customer deposit)
    Outgoing = 2    // money left pooling (a withdrawal we initiated)
}

public class BankStatementEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public StatementDirection Direction { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "EUR";
    public string CounterpartyIban { get; init; } = default!;
    public string CounterpartyName { get; init; } = default!;
    public string? Reference { get; init; }   // echoes our Transaction.Id on withdrawals
    public DateTime BookedAt { get; init; } = DateTime.UtcNow;
    public bool Processed { get; set; }       // set by our poller once ingested
}