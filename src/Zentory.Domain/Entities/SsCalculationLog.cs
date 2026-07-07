using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class SsCalculationLog : BaseEntity
{
    public Guid     OrganizationId     { get; private set; }
    public Guid     UserId             { get; private set; }  // quien ejecutó el cálculo
    public Guid?    CollaboratorId     { get; private set; }  // NULL = cálculo propio del freelance

    public string   CountryCode        { get; private set; } = default!;
    public string   Period             { get; private set; } = default!;  // 'YYYY-MM'
    public decimal  Income             { get; private set; }
    public string   Currency           { get; private set; } = default!;

    public string   Result             { get; private set; } = default!;  // JSONB snapshot
    public decimal  TotalContribution  { get; private set; }
    public decimal? SmlvUsed           { get; private set; }

    public string    Status              { get; private set; } = "calculado";
    // 'calculado' | 'marcado_radicado' — ver MarkFiled(). Solo aplica al cálculo propio
    // del freelance (CollaboratorId == null); el flujo de equipo usa PilaVerification.
    public DateTime? FiledAt             { get; private set; }
    public string?   DocumentUrl         { get; private set; }  // R2 object key del comprobante
    public string?   DocumentFileName    { get; private set; }
    public long?     DocumentFileSize    { get; private set; }
    public string?   DocumentContentType { get; private set; }

    private SsCalculationLog() { }

    public static SsCalculationLog Create(
        Guid    organizationId,
        Guid    userId,
        string  countryCode,
        string  period,
        decimal income,
        string  currency,
        string  resultJson,
        decimal totalContribution,
        decimal? smlvUsed         = null,
        Guid?   collaboratorId    = null)
    {
        return new SsCalculationLog
        {
            OrganizationId    = organizationId,
            UserId            = userId,
            CollaboratorId    = collaboratorId,
            CountryCode       = countryCode,
            Period            = period,
            Income            = income,
            Currency          = currency,
            Result            = resultJson,
            TotalContribution = totalContribution,
            SmlvUsed          = smlvUsed
        };
    }

    // Recalcular un período ya guardado (el usuario ajustó el ingreso y volvió a
    // calcular) no debe perder si ya lo había marcado como radicado.
    public void Recalculate(decimal income, string resultJson, decimal totalContribution, decimal? smlvUsed)
    {
        Income            = income;
        Result            = resultJson;
        TotalContribution = totalContribution;
        SmlvUsed          = smlvUsed;
    }

    public void MarkFiled(
        string? documentUrl = null,
        string? fileName    = null,
        long?   fileSize    = null,
        string? contentType = null)
    {
        Status              = "marcado_radicado";
        FiledAt             = DateTime.UtcNow;
        DocumentUrl         = documentUrl;
        DocumentFileName    = fileName;
        DocumentFileSize    = fileSize;
        DocumentContentType = contentType;
    }
}
