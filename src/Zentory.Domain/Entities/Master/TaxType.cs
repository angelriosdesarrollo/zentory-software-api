namespace Zentory.Domain.Entities.Master;

public class TaxType
{
    public short   Id          { get; set; }
    public string  CountryCode { get; set; } = default!;
    public string  Name        { get; set; } = default!;
    public string  Code        { get; set; } = default!;
    public decimal Rate        { get; set; }
    public string  AppliesTo  { get; set; } = "both";   // 'invoice' | 'expense' | 'both'
    public bool    Active      { get; set; } = true;
}
