namespace Zentory.Domain.Entities.Master;

public class ArlRiskLevel
{
    public short   Level       { get; set; }  // SMALLINT PK (I-V)
    public string  Description { get; set; } = default!;
    public decimal Rate        { get; set; }
    public string  CountryCode { get; set; } = "CO";
}
