using StockPilot.Domain.Common;

namespace StockPilot.Domain.Auditing;

/// <summary>
/// Append-only technical audit trail ("who changed what, when, from where").
/// Distinct from the inventory ledger, which is a *business* record of stock movements.
/// TenantId is nullable because platform actions (suspending a company) have no tenant.
/// </summary>
public sealed class AuditLog : Entity
{
    private AuditLog() { }

    public AuditLog(
        Guid? tenantId, Guid? userId, string action, string entityType, string? entityId,
        string? changesJson, string? ipAddress, string? correlationId, DateTimeOffset occurredAt)
    {
        TenantId = tenantId;
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Changes = changesJson;
        IpAddress = ipAddress;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }

    /// <summary>"Created" / "Updated" / "Deleted" for entity changes, or an event name like "Auth.LoginFailed".</summary>
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }

    /// <summary>JSON: { "Property": { "old": ..., "new": ... } }. Stored as jsonb.</summary>
    public string? Changes { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
