using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class OAuthAccount : BaseEntity
{
    public Guid     UserId           { get; private set; }
    public string   Provider         { get; private set; } = default!;  // 'google' | 'microsoft'
    public string   ProviderUserId   { get; private set; } = default!;
    public string?  AccessToken      { get; private set; }
    public string?  RefreshToken     { get; private set; }
    public DateTime? ExpiresAt       { get; private set; }

    private OAuthAccount() { }

    public static OAuthAccount Create(
        Guid    userId,
        string  provider,
        string  providerUserId,
        string? accessToken  = null,
        string? refreshToken = null,
        DateTime? expiresAt  = null)
    {
        return new OAuthAccount
        {
            UserId         = userId,
            Provider       = provider,
            ProviderUserId = providerUserId,
            AccessToken    = accessToken,
            RefreshToken   = refreshToken,
            ExpiresAt      = expiresAt
        };
    }

    public void UpdateTokens(string? accessToken, string? refreshToken, DateTime? expiresAt)
    {
        AccessToken  = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt    = expiresAt;
        UpdatedAt    = DateTime.UtcNow;
    }
}
