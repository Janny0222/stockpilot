using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth.Common;

/// <summary>
/// The result of a successful login or refresh. The controller returns the access token in the body
/// and puts the refresh token in an HttpOnly cookie. JavaScript never sees the refresh token.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Issues an access token plus a new refresh token in the given family. Caller saves changes.</summary>
public sealed class AuthTokenIssuer(
    IAppDbContext db,
    IAccessTokenService accessTokens,
    ISecureTokenFactory tokenFactory,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public (AuthResult Result, RefreshToken Entity) Issue(User user, Guid familyId, string? ipAddress, string? userAgent)
    {
        var access = accessTokens.Create(user);
        var (raw, hash) = tokenFactory.Create();
        var refresh = RefreshToken.Issue(
            user.Id, hash, familyId, clock.GetUtcNow(), options.Value.RefreshTokenLifetime, ipAddress, userAgent);

        db.RefreshTokens.Add(refresh);

        return (new AuthResult(access.Token, access.ExpiresAt, raw, refresh.ExpiresAt), refresh);
    }
}
