namespace Zentory.Application.Common.Options;

// Retention:* (PLAN-0018 RF-79, ADR purga §7). LegalDocumentsYears es cuánto se conserva un documento emitido en el
// archivo legal después de su emisión (RetainUntil = IssuedAt + LegalDocumentsYears). AddInfrastructure lo enlaza y lo
// valida al arrancar junto con Organizations:PurgeAfterDays: un plazo menor que 1 año borraría el archivo en la
// siguiente corrida de la purga.
public sealed class RetentionOptions
{
    public const string SectionName                = "Retention";
    public const string LegalDocumentsYearsKey     = SectionName + ":" + nameof(LegalDocumentsYears);
    public const int    DefaultLegalDocumentsYears = 5;
    public const int    MinimumLegalDocumentsYears = 1;

    public const string InvalidLegalDocumentsYearsMessage =
        LegalDocumentsYearsKey + " debe ser un entero mayor o igual que 1.";

    public int LegalDocumentsYears { get; set; } = DefaultLegalDocumentsYears;

    public bool IsValid => LegalDocumentsYears >= MinimumLegalDocumentsYears;
}
