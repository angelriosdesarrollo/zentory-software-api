namespace Zentory.Domain.Entities.Master;

public class ExchangeRate
{
    public Guid     Id           { get; set; } = Guid.NewGuid();
    public string   FromCurrency { get; set; } = default!;
    public string   ToCurrency   { get; set; } = default!;
    public decimal  Rate         { get; set; }
    public string   Source       { get; set; } = "fixer.io";
    public DateOnly RateDate     { get; set; }
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
}
