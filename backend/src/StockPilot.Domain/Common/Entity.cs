namespace StockPilot.Domain.Common;

/// <summary>
/// Base for every entity. IDs are UUID v7: time-ordered (index friendly) and
/// non-sequential (an attacker cannot guess the next ID, which is defence-in-depth against IDOR).
/// The ID is assigned in memory, so it is known before SaveChanges (useful for audit logs and links).
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

/// <summary>
/// Entities whose creation and modification metadata is stamped automatically by
/// the persistence layer (see AuditableEntityInterceptor). Domain code never sets these.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
}

/// <summary>
/// Marker for rows that belong to exactly one company. Every such entity gets:
/// 1) an EF global query filter (reads only see the current tenant), and
/// 2) a SaveChanges guard (writes to another tenant's rows throw).
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>Convenience base for tenant-owned, audited entities.</summary>
public abstract class TenantEntity : AuditableEntity, ITenantOwned
{
    protected TenantEntity() { }

    protected TenantEntity(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        TenantId = tenantId;
    }

    public Guid TenantId { get; private set; }
}
