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
    Guid? RoleId { get; }
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

/// <summary>
/// File bytes (product images). The database-backed implementation stages the file in the current
/// unit of work, so it commits atomically with the metadata row. An object-store implementation
/// (S3/R2) would upload immediately; a failed save then leaves an orphan, which is harmless.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(Guid tenantId, string key, string contentType, byte[] data, CancellationToken cancellationToken);
    Task<StoredFileContent?> ReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record StoredFileContent(byte[] Data, string ContentType);

/// <summary>
/// Pessimistic locking for stock balances. Ensures a WarehouseStock row exists for every (warehouse, variant)
/// pair, then locks them all with SELECT … FOR UPDATE in ascending ID order and returns them tracked.
/// Must be called inside IAppDbContext.ExecuteInTransactionAsync; the locks are held until commit.
///
/// Why ordered? Request A locks rows 1 then 2 while request B locks 2 then 1 → deadlock. If everyone locks
/// in the same (ID) order, the second request simply waits for the first.
/// </summary>
public interface IStockLocker
{
    Task<IReadOnlyDictionary<(Guid WarehouseId, Guid VariantId), Domain.Inventory.WarehouseStock>> LockAsync(
        Guid tenantId, IReadOnlyCollection<(Guid WarehouseId, Guid VariantId)> keys, CancellationToken cancellationToken);
}

/// <summary>Per-company sequential document numbers: TR-000001, TR-000002… Gap-free within a committed transaction.</summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(Guid tenantId, string prefix, CancellationToken cancellationToken);
}

public sealed record ProcessedImage(byte[] Data, string ContentType, int Width, int Height);

/// <summary>Validates an uploaded image by content (not file name) and re-encodes it safely.</summary>
public interface IImageProcessor
{
    Task<ProcessedImage> ProcessAsync(Stream input, CancellationToken cancellationToken);
}

/// <summary>A notification as pushed live to its recipient (same shape the list endpoint returns, plus who gets it).</summary>
public sealed record RealtimeNotification(
    Guid RecipientUserId, Guid Id, Domain.Notifications.NotificationType Type, string Title, string Message,
    string? EntityType, Guid? EntityId, DateTimeOffset CreatedAt);

/// <summary>
/// Live delivery of notifications that are ALREADY committed (SignalR in the API, a no-op elsewhere). Best effort: a
/// failed or missed push loses nothing, because the bell's list and unread count are read from the database.
/// </summary>
public interface IRealtimeNotifier
{
    Task PushAsync(IReadOnlyList<RealtimeNotification> notifications, CancellationToken cancellationToken);
}
