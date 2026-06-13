namespace Zentory.Domain.Entities;

public class RefreshToken
{
    public Guid     Id        { get; private set; } = Guid.NewGuid();
    public Guid     UserId    { get; private set; }
    public string   Token     { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public bool     IsRevoked { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string token, DateTime expiresAt)
    {
        return new RefreshToken
        {
            UserId    = userId,
            Token     = token,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke() => IsRevoked = true;
}
