using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Auditing;

namespace StockPilot.Application.Common.Auditing;

/// <summary>
/// Security-relevant events that are not plain entity changes (entity changes are captured
/// automatically by the persistence interceptor).
/// </summary>
public static class AuditEvents
{
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string RefreshTokenReuseDetected = "Auth.RefreshTokenReuseDetected";
    public const string PasswordReset = "Auth.PasswordReset";
    public const string LoggedOutEverywhere = "Auth.LoggedOutEverywhere";

    public static void Record(
        this IAppDbContext db, ICurrentUser currentUser, TimeProvider clock,
        string action, Guid? tenantId, Guid? userId, string? detailJson = null) =>
        db.AuditLogs.Add(new AuditLog(
            tenantId, userId, action, "User", userId?.ToString(), detailJson,
            currentUser.IpAddress, currentUser.CorrelationId, clock.GetUtcNow()));
}
