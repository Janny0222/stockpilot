using System.Diagnostics;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Infrastructure.Identity;

namespace StockPilot.API.Common;

/// <summary>
/// Reads identity from the authenticated principal (populated by JwtBearer only after signature,
/// issuer, audience, lifetime and session checks pass). Never reads tenant from headers/route/body:
/// that is what makes tenant spoofing impossible.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId => ReadGuid(StockPilotClaims.UserId);
    public Guid? TenantId => ReadGuid(StockPilotClaims.TenantId);
    public Guid? RoleId => ReadGuid(StockPilotClaims.RoleId);
    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true;
    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();
    public string? UserAgent => Context?.Request.Headers.UserAgent.ToString();
    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? Context?.TraceIdentifier;

    private Guid? ReadGuid(string claimType) =>
        IsAuthenticated && Guid.TryParse(Context!.User.FindFirst(claimType)?.Value, out var value) ? value : null;
}
