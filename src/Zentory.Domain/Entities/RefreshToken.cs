using System.Security.Cryptography;
using System.Text;

namespace Zentory.Domain.Entities;

// PLAN-0018 RF-30 (M2, D-12): la base guarda solo el SHA-256 del refresh token, como PasswordResetToken y
// EmailVerificationToken. Create recibe el token en claro y lo hashea aquí, para que ningún llamador pueda guardarlo
// sin hashear. IsRevoked es token de concurrencia (SR-0018.13): de dos rotaciones del mismo token solo una guarda.
public class RefreshToken
{
    public Guid     Id        { get; private set; } = Guid.NewGuid();
    public Guid     UserId    { get; private set; }
    public string   TokenHash { get; private set; } = default!;  // SHA-256
    public DateTime ExpiresAt { get; private set; }
    public bool     IsRevoked { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    // Sesión a la que pertenece (PLAN-0018 RF-90 a, D-47): el sid del access token emitido con él. Se fija al crearlo y
    // nunca cambia; la rotación la conserva. Null en los tokens anteriores a M2c, que son un refresh inválido (RNF-8).
    public Guid?    SessionId { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string rawToken, DateTime expiresAt, Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A refresh token belongs to a session.", nameof(sessionId));

        return new RefreshToken
        {
            UserId    = userId,
            TokenHash = HashOf(rawToken),
            ExpiresAt = expiresAt,
            SessionId = sessionId
        };
    }

    // SHA-256 en hexadecimal (64 caracteres), el mismo formato de los demás tokens de un solo uso.
    public static string HashOf(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public bool IsExpired(DateTime nowUtc) => ExpiresAt <= nowUtc;

    public void Revoke() => IsRevoked = true;
}
