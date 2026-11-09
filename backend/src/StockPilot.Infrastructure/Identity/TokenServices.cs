using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Domain.Identity;

namespace StockPilot.Infrastructure.Identity;

/// <summary>Custom claim names. Short, because they travel on every request.</summary>
public static class StockPilotClaims
{
    public const string UserId = JwtRegisteredClaimNames.Sub;
    public const string TenantId = "tid";
    public const string RoleId = "rid";
    public const string SecurityStamp = "sstamp";
}

/// <summary>
/// Issues short-lived (15 min) HS256 JWTs. Deliberately NOT in the token: permissions (looked up
/// server-side and cached, so revoking a permission doesn't wait for token expiry) and PII.
/// </summary>
internal sealed class JwtAccessTokenService(
    IOptions<JwtOptions> jwtOptions,
    IOptions<AuthOptions> authOptions,
    TimeProvider clock) : IAccessTokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(User user)
    {
        var now = clock.GetUtcNow();
        var expires = now.Add(authOptions.Value.AccessTokenLifetime);
        var jwt = jwtOptions.Value;

        var claims = new List<Claim>
        {
            new(StockPilotClaims.UserId, user.Id.ToString()),
            new(StockPilotClaims.RoleId, user.RoleId.ToString()),
            new(StockPilotClaims.SecurityStamp, user.SecurityStamp),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (user.TenantId is { } tenantId)
        {
            claims.Add(new Claim(StockPilotClaims.TenantId, tenantId.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}

/// <summary>
/// 256-bit random tokens (base64url) for refresh/email links; stored as SHA-256 hex.
/// A fast hash (not PBKDF2) is correct here: the input is already high-entropy random data,
/// so it can't be brute-forced the way a human password can.
/// </summary>
internal sealed class SecureTokenFactory : ISecureTokenFactory
{
    public (string RawToken, string TokenHash) Create()
    {
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

/// <summary>Wraps ASP.NET Core Identity's PasswordHasher (PBKDF2-HMAC-SHA512, 100k iterations, random salt).</summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object Subject = new();
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _inner = new();

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public PasswordCheck Verify(string passwordHash, string providedPassword) =>
        _inner.VerifyHashedPassword(Subject, passwordHash, providedPassword) switch
        {
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success => PasswordCheck.Success,
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
}
