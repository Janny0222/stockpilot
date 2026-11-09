using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth;

/// <summary>A signed-in device/browser. Id is the refresh-token family ID (stable across rotations).</summary>
public sealed record SessionDto(
    Guid Id,
    DateTimeOffset LastActiveAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    bool IsCurrent);

public sealed class ListSessionsHandler(
    IAppDbContext db, ICurrentUser currentUser, ISecureTokenFactory tokenFactory, TimeProvider clock)
{
    public async Task<IReadOnlyList<SessionDto>> Handle(string? currentRefreshToken, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("UNAUTHENTICATED", "Not signed in.");
        var now = clock.GetUtcNow();
        var currentHash = string.IsNullOrEmpty(currentRefreshToken) ? null : tokenFactory.Hash(currentRefreshToken);

        // Each family has at most one un-revoked token (rotation revokes the previous one).
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return active
            .Select(t => new SessionDto(t.FamilyId, t.CreatedAt, t.ExpiresAt, t.CreatedByIp, t.UserAgent, t.TokenHash == currentHash))
            .ToList();
    }
}

public sealed class RevokeSessionHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task Handle(Guid sessionId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("UNAUTHENTICATED", "Not signed in.");

        // Filtering by UserId is the IDOR guard: you can only revoke YOUR sessions.
        // Someone else's session ID is indistinguishable from a non-existent one (404).
        var tokens = await db.RefreshTokens
            .Where(t => t.FamilyId == sessionId && t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            throw new NotFoundException("Session", sessionId);
        }

        var now = clock.GetUtcNow();
        tokens.ForEach(t => t.Revoke(now, RefreshTokenRevokeReason.SessionRevoked));
        await db.SaveChangesAsync(ct);
    }
}
