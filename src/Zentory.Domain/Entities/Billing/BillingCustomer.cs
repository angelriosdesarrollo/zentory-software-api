using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class BillingCustomer : BaseEntity
{
    public Guid     OrganizationId     { get; private set; }
    public string   PaymentGateway     { get; private set; } = "stripe";
    // 'stripe' | 'mercadopago' | 'wompi'
    public string   GatewayCustomerId  { get; private set; } = default!;  // cus_xxx en Stripe
    public string   Email              { get; private set; } = default!;
    public string   Name               { get; private set; } = default!;
    public string?  CountryCode        { get; private set; }

    private BillingCustomer() { }

    public static BillingCustomer Create(
        Guid   organizationId,
        string gatewayCustomerId,
        string email,
        string name,
        string paymentGateway = "stripe",
        string? countryCode   = null)
    {
        return new BillingCustomer
        {
            OrganizationId    = organizationId,
            GatewayCustomerId = gatewayCustomerId,
            Email             = email,
            Name              = name,
            PaymentGateway    = paymentGateway,
            CountryCode       = countryCode
        };
    }

    public void UpdateContact(string email, string name) { Email = email; Name = name; UpdatedAt = DateTime.UtcNow; }
}
