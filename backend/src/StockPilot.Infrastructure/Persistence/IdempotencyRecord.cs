using StockPilot.Domain.Common;

namespace StockPilot.Infrastructure.Persistence;

/// <summary>
/// Remembers the outcome of a write request by its Idempotency-Key, so a retried request (double-click,
/// flaky network, client retry) returns the original result instead of moving stock twice.
/// A row with a null <see cref="StatusCode"/> means "in progress".
/// </summary>
public sealed class IdempotencyRecord : ITenantOwned
{
    public const int KeyMaxLength = 64;

    private IdempotencyRecord() { }

    public IdempotencyRecord(Guid tenantId, string key, Guid? userId, string requestHash, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Key = key;
        UserId = userId;
        RequestHash = requestHash;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }

    /// <summary>SHA-256 of the request. The same key with a DIFFERENT body is a client bug, rejected with 422.</summary>
    public string RequestHash { get; private set; } = string.Empty;
    public int? StatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
