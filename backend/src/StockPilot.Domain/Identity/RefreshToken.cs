using StockPilot.Domain.Common;

namespace StockPilot.Domain.Identity;

public enum RefreshTokenRevokeReason
{
    Rotated = 1,
    Logout = 2,
    LogoutAll = 3,
    ReuseDetected = 4,
    PasswordChanged = 5,
    SessionRevoked = 6,
    UserDeactivated = 7,
}

/// <summary>
/// One link in a refresh-token rotation chain ("family").
///
/// Rotation: every successful refresh revokes the presented token (reason Rotated) and issues a
/// new one in the same family. If an already-rotated token is presented again, someone replayed a
/// copy, possibly an attacker who stole it, so the WHOLE family is revoked and both parties must sign in.
///
/// Only a SHA-256 hash of the token is stored. A database leak therefore does not leak usable tokens.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public static RefreshToken Issue(
        Guid userId, string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime,
        string? ipAddress, string? userAgent) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        FamilyId = familyId,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
        CreatedByIp = ipAddress,
        UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent,
    };

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevokeReason? RevokeReason { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>Concurrency token (PostgreSQL xmin). Two parallel rotations of one token: one wins, one gets a conflict.</summary>
    public uint Version { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, RefreshTokenRevokeReason reason, Guid? replacedByTokenId = null)
    {
        if (RevokedAt is not null)
        {
            return; // idempotent: first revocation reason wins
        }

        RevokedAt = now;
        RevokeReason = reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}
