namespace Ledger.Customers;

public class CustomerRegistry : ICustomerRegistry
{
    private readonly List<Customer> _customers = new();
    private readonly object _lock = new();

    public CustomerRegistry()
    {
        // Seed a test customer so we have something to work with
        _customers.Add(new Customer
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Alice Muller",
            Iban = "DE89370400440532013000"
        });
    }

    public Customer? FindByIban(string iban)
    {
        lock (_lock)
            return _customers.FirstOrDefault(c =>
                string.Equals(c.Iban, iban, StringComparison.OrdinalIgnoreCase));
    }

    public Customer? FindById(Guid id)
    {
        lock (_lock) return _customers.FirstOrDefault(c => c.Id == id);
    }

    public void Add(Customer customer)
    {
        lock (_lock) _customers.Add(customer);
    }

    public IReadOnlyList<Customer> All()
    {
        lock (_lock) return _customers.ToList();
    }
}