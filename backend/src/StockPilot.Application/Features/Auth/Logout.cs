using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Auditing;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth;

public sealed record LogoutCommand(string? RefreshToken);

/// <summary>Revokes the presented session (its whole rotation family). Idempotent: unknown tokens are ignored.</summary>
public sealed class LogoutHandler(IAppDbContext db, ISecureTokenFactory tokenFactory, TimeProvider clock)
{
    public async Task Handle(LogoutCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return;
        }

        var hash = tokenFactory.Hash(command.RefreshToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var tokens = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        tokens.ForEach(t => t.Revoke(now, RefreshTokenRevokeReason.Logout));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// "Sign out everywhere": revokes every refresh token AND rotates the security stamp, which makes
/// already-issued access tokens fail validation within the session-validation cache window.
/// </summary>
public sealed class LogoutAllHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task Handle(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("UNAUTHENTICATED", "Not signed in.");
        var now = clock.GetUtcNow();

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId, ct);
        user.RotateSecurityStamp();

        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        tokens.ForEach(t => t.Revoke(now, RefreshTokenRevokeReason.LogoutAll));

        db.Record(currentUser, clock, AuditEvents.LoggedOutEverywhere, user.TenantId, user.Id);
        await db.SaveChangesAsync(ct);
    }
}
