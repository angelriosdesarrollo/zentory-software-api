using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class OrganizationIntegration : BaseEntity
{
    public Guid      OrganizationId         { get; private set; }
    public string    IntegrationId          { get; private set; } = default!;
    public bool      IsActive               { get; private set; }
    public DateTime? ConnectedAt            { get; private set; }
    public Guid?     ConnectedByUserId      { get; private set; }
    public string?   ConnectedAs            { get; private set; }
    public string?   ExternalAccountId      { get; private set; }
    public string?   ExternalWorkspaceId    { get; private set; }
    public string?   AccessTokenEncrypted   { get; private set; }
    public string?   RefreshTokenEncrypted  { get; private set; }
    public DateTime? TokenExpiresAt         { get; private set; }
    public string?   Scopes                 { get; private set; }
    public string?   Metadata               { get; private set; }

    public IntegrationCatalog? Integration  { get; private set; }

    private OrganizationIntegration() { }

    public static OrganizationIntegration Create(Guid organizationId, string integrationId) =>
        new() { OrganizationId = organizationId, IntegrationId = integrationId, IsActive = false };

    public void Connect(
        Guid     connectedByUserId,
        string?  connectedAs           = null,
        string?  externalAccountId     = null,
        string?  externalWorkspaceId   = null,
        string?  accessTokenEncrypted  = null,
        string?  refreshTokenEncrypted = null,
        DateTime? tokenExpiresAt       = null,
        string?  scopes                = null,
        string?  metadata              = null)
    {
        IsActive              = true;
        ConnectedAt           = DateTime.UtcNow;
        ConnectedByUserId     = connectedByUserId;
        ConnectedAs           = connectedAs;
        ExternalAccountId     = externalAccountId;
        ExternalWorkspaceId   = externalWorkspaceId;
        AccessTokenEncrypted  = accessTokenEncrypted;
        RefreshTokenEncrypted = refreshTokenEncrypted;
        TokenExpiresAt        = tokenExpiresAt;
        Scopes                = scopes;
        Metadata              = metadata;
        UpdatedAt             = DateTime.UtcNow;
    }

    public void Disconnect()
    {
        IsActive              = false;
        AccessTokenEncrypted  = null;
        RefreshTokenEncrypted = null;
        TokenExpiresAt        = null;
        UpdatedAt             = DateTime.UtcNow;
    }
}
