namespace StockPilot.Application.Common.Exceptions;

/// <summary>
/// Application-level failures. Each carries a stable Code for the frontend.
/// The API's global exception handler maps each type to an HTTP status
/// (see StockPilot.API/Middleware/GlobalExceptionHandler.cs), so handlers simply throw.
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>404. Also used for "exists but belongs to another tenant", so existence is not leaked.</summary>
public sealed class NotFoundException(string resource, object key)
    : AppException("NOT_FOUND", $"{resource} '{key}' was not found.");

/// <summary>401. Bad credentials, invalid or expired refresh token.</summary>
public sealed class AuthenticationFailedException(string code, string message)
    : AppException(code, message);

/// <summary>403. Authenticated, but not allowed (unverified email, suspended company, missing permission).</summary>
public sealed class ForbiddenException(string code, string message)
    : AppException(code, message);

/// <summary>
/// A write touched a row of a different tenant than the caller's. This should be impossible through
/// normal code paths; reaching it means a bug or an attack, so it is logged loudly and the API returns
/// a generic 403.
/// </summary>
public sealed class TenantIsolationException(string message)
    : AppException("TENANT_ISOLATION_VIOLATION", message);

/// <summary>409. Duplicate (email taken), stale update, or state conflict.</summary>
public sealed class ConflictException(string code, string message)
    : AppException(code, message);
