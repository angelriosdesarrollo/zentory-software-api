using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Token de verificación de email (PLAN-0018 RF-20, RF-22; D-11): la base guarda solo el SHA-256 del token y el email al
// que se envió. Un solo uso con canje atómico: UsedAt es token de concurrencia (EmailVerificationTokenConfiguration), así
// que de dos canjes simultáneos del mismo token solo uno guarda y el otro recibe DbUpdateConcurrencyException. Emitir uno
// nuevo invalida los anteriores sin usar del usuario.
public class EmailVerificationToken : BaseEntity
{
    // Vigencia del enlace de verificación (RF-22 b).
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public Guid     UserId    { get; private set; }
    public string   Email     { get; private set; } = default!;
    public string   TokenHash { get; private set; } = default!;  // SHA-256
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt   { get; private set; }

    private EmailVerificationToken() { }

    public static EmailVerificationToken Create(
        Guid    userId,
        string  email,
        string  tokenHash,
        DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new EmailVerificationToken
        {
            UserId    = userId,
            Email     = email,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        };
    }

    // Token nuevo para el email actual del usuario, con la vigencia de RF-22 b contada desde nowUtc.
    public static EmailVerificationToken Issue(Guid userId, string email, string tokenHash, DateTime nowUtc)
        => Create(userId, email, tokenHash, nowUtc.Add(Lifetime));

    public bool IsValid() => IsValidAt(DateTime.UtcNow);

    public bool IsValidAt(DateTime nowUtc) => UsedAt is null && nowUtc < ExpiresAt;

    // El token prueba control del buzón al que se envió: si el usuario cambió de email después, ya no lo verifica.
    public bool IsFor(string email) => string.Equals(Email, email, StringComparison.Ordinal);

    public void MarkUsed() => MarkUsedAt(DateTime.UtcNow);

    public void MarkUsedAt(DateTime nowUtc)
    {
        UsedAt    = nowUtc;
        UpdatedAt = nowUtc;
    }

    // Al emitir uno nuevo, los anteriores sin usar quedan inservibles (RF-22 d). Idempotente: uno ya usado no cambia.
    public void Invalidate(DateTime nowUtc)
    {
        if (UsedAt is not null) return;
        MarkUsedAt(nowUtc);
    }
}
