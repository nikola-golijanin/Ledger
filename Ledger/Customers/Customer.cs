namespace Ledger.Customers;

public class Customer
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Iban { get; init; } = default!; // their private IBAN on file
}