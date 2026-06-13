using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class PasswordResetToken : BaseEntity
{
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
        return new PasswordResetToken
        {
            UserId    = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress
        };
    }

    public bool IsValid() => UsedAt is null && DateTime.UtcNow < ExpiresAt;

    public void MarkUsed() { UsedAt = DateTime.UtcNow; }
}
