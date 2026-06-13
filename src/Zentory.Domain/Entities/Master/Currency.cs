namespace Zentory.Domain.Entities.Master;

public class Currency
{
    public string  Code     { get; set; } = default!;  // CHAR(3) PK
    public string  Name     { get; set; } = default!;
    public string  Symbol   { get; set; } = default!;
    public short   Decimals { get; set; } = 2;
    public bool    Active   { get; set; } = true;
}
