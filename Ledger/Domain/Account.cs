namespace Ledger.Domain;

public class Account
{
    public int Number { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public AccountType Type { get; set; }
    public int? ParentNumber { get; set; }
    public bool IsPostable { get; set; } = true;   // false for rollup parents

    public Account? Parent { get; set; }
}