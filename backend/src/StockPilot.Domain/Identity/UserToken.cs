using StockPilot.Domain.Common;

namespace StockPilot.Domain.Identity;

public enum UserTokenPurpose
{
    EmailVerification = 1,
    PasswordReset = 2,
}

/// <summary>
/// Single-use, expiring token sent by email (verify address / reset password).
/// Stored hashed, like refresh tokens. The raw value only ever exists in the email link.
/// </summary>
public sealed class UserToken : Entity
{
    private UserToken() { }

    public static UserToken Create(Guid userId, UserTokenPurpose purpose, string tokenHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        UserId = userId,
        Purpose = purpose,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
    };

    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public UserTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new DomainException("TOKEN_INVALID", "This link is invalid or has expired.");
        }

        ConsumedAt = now;
    }
}
