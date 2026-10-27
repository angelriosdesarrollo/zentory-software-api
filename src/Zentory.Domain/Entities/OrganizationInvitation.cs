using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Invitación a una organización (PLAN-0018 RF-49 b; D-20; migración M4). La base guarda solo el SHA-256 del token
// (OneTimeToken) y el email normalizado del invitado. AcceptedAt es token de concurrencia
// (OrganizationInvitationConfiguration): de dos aceptaciones simultáneas solo una guarda (T29b). Una invitación está
// pendiente mientras no se aceptó, no se revocó, no se borró y no venció; solo las pendientes cuentan para
// max_org_members (RF-49 e).
public class OrganizationInvitation : TenantEntity
{
    // Vigencia del enlace de invitación (RF-49 c).
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public string    Email      { get; private set; } = default!;
    public string    Role       { get; private set; } = default!;  // Un valor de OrgRole.Assignable
    public string    TokenHash  { get; private set; } = default!;  // SHA-256
    public DateTime  ExpiresAt  { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public Guid      InvitedBy  { get; private set; }
    public DateTime? RevokedAt  { get; private set; }
    public DateTime? DeletedAt  { get; private set; }

    private OrganizationInvitation() { }

    // Invitación nueva con la vigencia de RF-49 c contada desde nowUtc. email ya llega normalizado (EmailAddress) y role
    // ya validado contra OrgRole.Assignable: la entidad no decide reglas de negocio del comando.
    public static OrganizationInvitation Issue(
        Guid     organizationId,
        string   email,
        string   role,
        string   tokenHash,
        Guid     invitedBy,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email          = email,
            Role           = role,
            TokenHash      = tokenHash,
            InvitedBy      = invitedBy,
            ExpiresAt      = nowUtc.Add(Lifetime),
            CreatedAt      = nowUtc,
            UpdatedAt      = nowUtc,
        };
    }

    public bool IsPendingAt(DateTime nowUtc)
        => AcceptedAt is null && RevokedAt is null && DeletedAt is null && nowUtc < ExpiresAt;

    // Revocación en el origen (PLAN-0018 REV-0005 H-5): quien la emitió dejó de ser owner o admin. Idempotente.
    public void Revoke(DateTime nowUtc)
    {
        if (RevokedAt is not null) return;
        RevokedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    // Canje de la invitación (RF-50; T29b). Quien la canjea comprobó antes IsPendingAt; AcceptedAt es token de
    // concurrencia, así que de dos canjes simultáneos solo uno guarda y el otro recibe DbUpdateConcurrencyException.
    public void Accept(DateTime nowUtc)
    {
        if (AcceptedAt is not null)
            throw new InvalidOperationException("La invitación ya fue aceptada.");
        AcceptedAt = nowUtc;
        UpdatedAt  = nowUtc;
    }
}
