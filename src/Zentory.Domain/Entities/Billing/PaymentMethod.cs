using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class PaymentMethod : BaseEntity
{
    public Guid     CustomerId     { get; private set; }
    public string   GatewayPmId   { get; private set; } = default!;  // pm_xxx en Stripe
    public string   Type          { get; private set; } = default!;   // 'card' | 'pse' | 'sepa_debit'
    public string?  Brand         { get; private set; }   // 'visa' | 'mastercard'
    public string?  LastFour      { get; private set; }
    public short?   ExpMonth      { get; private set; }
    public short?   ExpYear       { get; private set; }
    public bool     IsDefault     { get; private set; }
    public DateTime? DeletedAt    { get; private set; }

    private PaymentMethod() { }

    public static PaymentMethod Create(
        Guid    customerId,
        string  gatewayPmId,
        string  type,
        string? brand    = null,
        string? lastFour = null,
        short?  expMonth = null,
        short?  expYear  = null,
        bool    isDefault= false)
    {
        return new PaymentMethod
        {
            CustomerId   = customerId,
            GatewayPmId  = gatewayPmId,
            Type         = type,
            Brand        = brand,
            LastFour     = lastFour,
            ExpMonth     = expMonth,
            ExpYear      = expYear,
            IsDefault    = isDefault
        };
    }

    public void SetDefault(bool value) { IsDefault = value; }
    public void SoftDelete()           { DeletedAt = DateTime.UtcNow; }
}
