using StockPilot.Domain.Identity;

namespace StockPilot.Application.Common.Abstractions;

/// <summary>
/// Who is making this request. Populated from the *validated* JWT, never from request input.
/// TenantId is the single source of truth for tenant isolation.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);
    PasswordCheck Verify(string passwordHash, string providedPassword);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenService
{
    AccessToken Create(User user);
}

/// <summary>Creates high-entropy opaque tokens (refresh, email links) and their storage hashes.</summary>
public interface ISecureTokenFactory
{
    (string RawToken, string TokenHash) Create();
    string Hash(string rawToken);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IPermissionService
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid roleId, CancellationToken cancellationToken);
}
