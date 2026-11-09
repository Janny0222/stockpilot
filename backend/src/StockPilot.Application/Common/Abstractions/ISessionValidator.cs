namespace StockPilot.Application.Common.Abstractions;

/// <summary>
/// Checks, on each authenticated request, that the access token's user is still active, its
/// security stamp still matches (no password change or sign-out-everywhere since issue), and its
/// company is not suspended. Results are cached briefly so this isn't a DB hit per request.
/// </summary>
public interface ISessionValidator
{
    Task<bool> IsValidAsync(Guid userId, string securityStamp, CancellationToken cancellationToken);
}
