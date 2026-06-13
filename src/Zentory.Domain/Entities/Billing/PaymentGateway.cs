using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class PaymentGateway : BaseEntity
{
    public string   Key                 { get; private set; } = default!;  // 'stripe' | 'mercadopago' | 'wompi'
    public string   DisplayName         { get; private set; } = default!;
    public bool     IsActive            { get; private set; }
    public bool     IsDefault           { get; private set; }
    public string   ApiKeyEnv           { get; private set; } = default!;      // nombre de env var
    public string   WebhookSecretEnv    { get; private set; } = default!;
    public string?  PublicKey           { get; private set; }
    public string[] SupportedCountries  { get; private set; } = [];
    public string[] SupportedCurrencies { get; private set; } = [];
    public string   WebhookPath         { get; private set; } = default!;
    public short    SortOrder           { get; private set; }

    private PaymentGateway() { }

    public static PaymentGateway Create(
        string   key,
        string   displayName,
        string   apiKeyEnv,
        string   webhookSecretEnv,
        string   webhookPath,
        string?  publicKey           = null,
        string[]? supportedCountries  = null,
        string[]? supportedCurrencies = null,
        short    sortOrder           = 0)
    {
        return new PaymentGateway
        {
            Key                 = key,
            DisplayName         = displayName,
            ApiKeyEnv           = apiKeyEnv,
            WebhookSecretEnv    = webhookSecretEnv,
            WebhookPath         = webhookPath,
            PublicKey           = publicKey,
            SupportedCountries  = supportedCountries ?? [],
            SupportedCurrencies = supportedCurrencies ?? [],
            SortOrder           = sortOrder
        };
    }

    public void Activate()   { IsActive = true;  UpdatedAt = DateTime.UtcNow; }
    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
    public void SetDefault(bool value) { IsDefault = value; UpdatedAt = DateTime.UtcNow; }
}
