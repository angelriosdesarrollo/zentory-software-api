using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Magic link de acceso al portal de autoservicio de colaboradores (Camino B: el
// colaborador entra por su cuenta, sin que la empresa se lo pida). Mismo patrón que
// PasswordResetToken: hash del token (nunca se guarda en claro), un solo uso real
// (a diferencia de PilaVerification.Token/CollaboratorPayoutInvoice.PublicToken, que
// son reusables hasta expirar porque representan una solicitud recurrente, no un login).
public class CollaboratorAccessToken : BaseEntity
{
    public string   Email     { get; private set; } = default!;  // normalizado a lowercase
    public string   TokenHash { get; private set; } = default!;  // SHA-256
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt   { get; private set; }

    private CollaboratorAccessToken() { }

    public static CollaboratorAccessToken Create(string email, string tokenHash, DateTime expiresAt)
    {
        return new CollaboratorAccessToken
        {
            Email     = email,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        };
    }

    public bool IsValid() => UsedAt is null && DateTime.UtcNow < ExpiresAt;

    public void MarkUsed() { UsedAt = DateTime.UtcNow; }
}
