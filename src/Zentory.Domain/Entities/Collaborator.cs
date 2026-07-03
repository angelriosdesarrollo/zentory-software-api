using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class Collaborator : TenantEntity
{
    public Guid?   UserId                   { get; private set; }  // NULL si externo sin cuenta
    public string  Name                     { get; private set; } = default!;
    public string? Email                    { get; private set; }
    public string? Phone                    { get; private set; }
    public string  Type                     { get; private set; } = default!;
    // 'employee' | 'hourly_contractor' | 'fixed_contractor'
    public string  Status                   { get; private set; } = "active";
    // 'active' | 'inactive' | 'terminated'
    public string? Role                     { get; private set; }  // cargo/puesto

    public decimal? HourlyRate              { get; private set; }
    public decimal? MonthlyRate             { get; private set; }  // para fixed_contractor
    public string   Currency                { get; private set; } = "COP";

    // PILA
    public string  PilaStatus              { get; private set; } = "no_aplica";
    // 'no_aplica' | 'pendiente' | 'solicitada' | 'recibida' | 'verificada'
    public string? PilaLastVerifiedPeriod  { get; private set; }  // 'YYYY-MM'
    public short?  ArlRiskLevel            { get; private set; }

    // Cuentas de cobro — resumen de la última, evita consultar CollaboratorPayoutInvoice
    // para listas rápidas (ver GetPilaComplianceQuery).
    public string  PayoutInvoiceStatus     { get; private set; } = "ninguna";
    // 'ninguna' | 'generated' | 'sent' | 'uploaded_manually'
    public string? PayoutInvoiceLastPeriod { get; private set; }  // 'YYYY-MM'

    public string? IdNumber                { get; private set; }  // CC / NIT

    public DateTime? DeletedAt             { get; private set; }

    private Collaborator() { }

    public static Collaborator Create(
        Guid     organizationId,
        string   name,
        string   type,
        string   currency = "COP",
        Guid?    userId       = null,
        string?  email        = null,
        string?  phone        = null,
        string?  role         = null,
        decimal? hourlyRate   = null,
        decimal? monthlyRate  = null,
        string?  idNumber     = null,
        short?   arlRiskLevel = null)
    {
        return new Collaborator
        {
            OrganizationId = organizationId,
            UserId         = userId,
            Name           = name,
            Type           = type,
            Currency       = currency,
            Email          = email,
            Phone          = phone,
            Role           = role,
            HourlyRate     = hourlyRate,
            MonthlyRate    = monthlyRate,
            IdNumber       = idNumber,
            ArlRiskLevel   = arlRiskLevel
        };
    }

    public void Update(
        string   name,
        string   type,
        string   status,
        string   currency,
        string?  email,
        string?  phone,
        string?  role,
        decimal? hourlyRate,
        decimal? monthlyRate,
        string?  idNumber,
        short?   arlRiskLevel)
    {
        Name         = name;
        Type         = type;
        Status       = status;
        Currency     = currency;
        Email        = email;
        Phone        = phone;
        Role         = role;
        HourlyRate   = hourlyRate;
        MonthlyRate  = monthlyRate;
        IdNumber     = idNumber;
        ArlRiskLevel = arlRiskLevel;
        UpdatedAt    = DateTime.UtcNow;
    }

    public void UpdatePilaStatus(string status, string? lastVerifiedPeriod = null)
    {
        PilaStatus             = status;
        PilaLastVerifiedPeriod = lastVerifiedPeriod ?? PilaLastVerifiedPeriod;
        UpdatedAt              = DateTime.UtcNow;
    }

    public void UpdatePayoutInvoiceStatus(string status, string? period = null)
    {
        PayoutInvoiceStatus     = status;
        PayoutInvoiceLastPeriod = period ?? PayoutInvoiceLastPeriod;
        UpdatedAt               = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}
