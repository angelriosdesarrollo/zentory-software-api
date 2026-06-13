using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

// Insert-only. Used for idempotency — prevents double-processing of gateway webhooks.
public class WebhookEvent : BaseEntity
{
    public string   PaymentGateway  { get; private set; } = "stripe";
    public string   GatewayEventId  { get; private set; } = default!;  // evt_xxx en Stripe
    public string   EventType       { get; private set; } = default!;
    public string   Payload         { get; private set; } = default!;  // JSONB as string
    public bool     Processed       { get; private set; }
    public DateTime? ProcessedAt    { get; private set; }
    public string?  ErrorMessage    { get; private set; }
    public short    RetryCount      { get; private set; }

    private WebhookEvent() { }

    public static WebhookEvent Create(string paymentGateway, string gatewayEventId, string eventType, string payloadJson)
    {
        return new WebhookEvent
        {
            PaymentGateway = paymentGateway,
            GatewayEventId = gatewayEventId,
            EventType      = eventType,
            Payload        = payloadJson
        };
    }

    public void MarkProcessed()
    {
        Processed    = true;
        ProcessedAt  = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkFailed(string error) { RetryCount++; ErrorMessage = error; }
}
