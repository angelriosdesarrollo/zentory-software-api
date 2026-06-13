namespace Zentory.Domain.Entities.Master;

public class Country
{
    public string Code         { get; set; } = default!;  // CHAR(2) PK
    public string Name         { get; set; } = default!;
    public string CurrencyCode { get; set; } = default!;
    public string Timezone     { get; set; } = "UTC";
    public string Locale       { get; set; } = "es";
    public bool   Active       { get; set; } = true;
}
