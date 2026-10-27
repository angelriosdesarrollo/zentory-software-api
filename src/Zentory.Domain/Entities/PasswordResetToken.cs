using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Token de reset de contraseña (PLAN-0018 RF-22; D-11): la base guarda solo el SHA-256 del token. Un solo uso con canje
// atómico: UsedAt es token de concurrencia (PasswordResetTokenConfiguration), así que de dos canjes simultáneos del mismo
// token solo uno guarda y el otro recibe DbUpdateConcurrencyException. Emitir uno nuevo invalida los anteriores sin usar.
public class PasswordResetToken : BaseEntity
{
    // Vigencia del enlace de reset (RF-22 b).
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public Guid     UserId    { get; private set; }
    public string   TokenHash { get; private set; } = default!;  // SHA-256
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt   { get; private set; }
    public string?  IpAddress { get; private set; }

    private PasswordResetToken() { }

    public static PasswordResetToken Create(
        Guid    userId,
        string  tokenHash,
        DateTime expiresAt,
        string? ipAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new PasswordResetToken
        {
            UserId    = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress
        };
    }

    // Token nuevo con la vigencia de RF-22 b contada desde nowUtc.
    public static PasswordResetToken Issue(Guid userId, string tokenHash, DateTime nowUtc, string? ipAddress = null)
        => Create(userId, tokenHash, nowUtc.Add(Lifetime), ipAddress);

    public bool IsValid() => IsValidAt(DateTime.UtcNow);

    public bool IsValidAt(DateTime nowUtc) => UsedAt is null && nowUtc < ExpiresAt;

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
