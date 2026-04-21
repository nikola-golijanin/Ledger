namespace Ledger.Customers;

public interface ICustomerRegistry
{
    Customer? FindByIban(string iban);
    Customer? FindById(Guid id);
    void Add(Customer customer);
    IReadOnlyList<Customer> All();
}