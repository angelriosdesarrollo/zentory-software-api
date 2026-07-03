using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class GatewayRoutingRule : BaseEntity
{
    public string   GatewayKey   { get; private set; } = default!;
    public short    Priority     { get; private set; }
    public string?  CountryCode  { get; private set; }   // NULL = todos
    public string?  Currency     { get; private set; }   // NULL = todas
    public string?  LegalType  { get; private set; }   // NULL = ambos | 'freelance' | 'empresa'
    public bool     IsActive     { get; private set; } = true;
    public DateTime ValidFrom    { get; private set; } = DateTime.UtcNow;
    public DateTime? ValidUntil  { get; private set; }
    public string   Reason       { get; private set; } = default!;

    private GatewayRoutingRule() { }

    public static GatewayRoutingRule Create(
        string  gatewayKey,
        short   priority,
        string  reason,
        string? countryCode  = null,
        string? currency     = null,
        string? legalType  = null,
        bool    isActive     = true,
        DateTime? validUntil = null)
    {
        return new GatewayRoutingRule
        {
            GatewayKey  = gatewayKey,
            Priority    = priority,
            Reason      = reason,
            CountryCode = countryCode,
            Currency    = currency,
            LegalType = legalType,
            IsActive    = isActive,
            ValidUntil  = validUntil
        };
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTime.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
}
