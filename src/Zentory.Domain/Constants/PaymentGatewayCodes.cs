namespace Zentory.Domain.Constants;

// Pasarelas de cobro de BillingCustomer.PaymentGateway (ADR-0002 §3; PLAN-0018 RF-37 b). Manual es la del plan asignado
// por seed assign-plan, sin pasarela de pago en el MVP; no prejuzga DEC-0003.
public static class PaymentGatewayCodes
{
    public const string Stripe      = "stripe";
    public const string MercadoPago = "mercadopago";
    public const string Wompi       = "wompi";
    public const string Manual      = "manual";
}
