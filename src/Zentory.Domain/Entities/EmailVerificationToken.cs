using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class EmailVerificationToken : BaseEntity
{
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
        return new EmailVerificationToken
        {
            UserId    = userId,
            Email     = email,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        };
    }

    public bool IsValid() => UsedAt is null && DateTime.UtcNow < ExpiresAt;

    public void MarkUsed() { UsedAt = DateTime.UtcNow; }
}
