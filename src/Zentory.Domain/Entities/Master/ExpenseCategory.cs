namespace Zentory.Domain.Entities.Master;

public class ExpenseCategory
{
    public short  Id       { get; set; }
    public string Name     { get; set; } = default!;
    public string Slug     { get; set; } = default!;
    public string Type     { get; set; } = default!;  // 'income' | 'expense' | 'both'
    public bool   IsSystem { get; set; } = true;
}
