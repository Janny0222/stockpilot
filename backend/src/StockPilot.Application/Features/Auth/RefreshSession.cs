using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Auditing;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Application.Features.Auth;

public sealed record RefreshSessionCommand(string? RefreshToken);

/// <summary>
/// Refresh-token rotation with reuse detection:
///   valid token        → revoke it (Rotated) and issue a new one in the same family
///   rotated &lt; grace ago → benign multi-tab race: 401, family untouched
///   any other reuse    → treat as theft: revoke the whole family
/// </summary>
public sealed class RefreshSessionHandler(
    IAppDbContext db,
    ISecureTokenFactory tokenFactory,
    AuthTokenIssuer tokenIssuer,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task<AuthResult> Handle(RefreshSessionCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            throw Invalid();
        }

        var now = clock.GetUtcNow();
        var hash = tokenFactory.Hash(command.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw Invalid();

        if (token.IsRevoked)
        {
            var isRecentRotation = token.RevokeReason == RefreshTokenRevokeReason.Rotated
                && now - token.RevokedAt!.Value < options.Value.RefreshTokenReuseGracePeriod;

            if (!isRecentRotation)
            {
                await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevokeReason.ReuseDetected, now, ct);
                db.Record(currentUser, clock, AuditEvents.RefreshTokenReuseDetected, tenantId: null, token.UserId,
                    $$"""{"familyId":"{{token.FamilyId}}"}""");
                await db.SaveChangesAsync(ct);
            }

            throw Invalid();
        }

        if (!token.IsActive(now))
        {
            throw Invalid();
        }

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == token.UserId, ct);
        var tenantActive = user.TenantId is not { } tenantId
            || await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, ct);

        if (!user.IsActive || !tenantActive)
        {
            await RevokeFamilyAsync(token.FamilyId, RefreshTokenRevokeReason.SessionRevoked, now, ct);
            await db.SaveChangesAsync(ct);
            throw Invalid();
        }

        var (result, newToken) = tokenIssuer.Issue(user, token.FamilyId, currentUser.IpAddress, currentUser.UserAgent);
        token.Revoke(now, RefreshTokenRevokeReason.Rotated, newToken.Id);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (ConflictException ex) when (ex.Code == "CONCURRENCY_CONFLICT")
        {
            // Another request rotated this exact token a moment ago (xmin changed). Same as the grace case.
            throw Invalid();
        }

        return result;
    }

    private async Task RevokeFamilyAsync(Guid familyId, RefreshTokenRevokeReason reason, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active)
        {
            t.Revoke(now, reason);
        }
    }

    private static AuthenticationFailedException Invalid() =>
        new("INVALID_REFRESH_TOKEN", "Your session has expired. Please sign in again.");
}
