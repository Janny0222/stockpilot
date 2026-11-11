using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Runs inside SaveChanges, so audit rows commit in the SAME transaction as the change they describe.
/// It can never record a change that rolled back, and never miss one that committed.
///  1. Stamps CreatedAt/By and UpdatedAt/By on AuditableEntity.
///  2. Writes an AuditLog row per created/updated/deleted entity with old→new values.
/// </summary>
internal sealed class AuditingInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    // Never written to the audit log: secrets, and high-churn bookkeeping covered by auth audit events.
    private static readonly HashSet<string> ExcludedProperties =
    [
        nameof(User.PasswordHash), nameof(User.SecurityStamp), nameof(RefreshToken.TokenHash),
        nameof(User.AccessFailedCount), nameof(User.LockoutEnd), nameof(User.LastLoginAt),
        nameof(AuditableEntity.CreatedAt), nameof(AuditableEntity.CreatedBy),
        nameof(AuditableEntity.UpdatedAt), nameof(AuditableEntity.UpdatedBy),
    ];

    // Entities that are logs/credentials themselves; auditing them is noise (or a leak).
    // StoredFile holds raw image bytes: auditing it would copy megabytes into audit_logs.
    // The inventory ledger IS an audit trail (who, what, when, before/after), and WarehouseStock is a snapshot
    // derived from it, so auditing either would only duplicate every stock movement.
    private static readonly HashSet<Type> NotAudited =
    [
        typeof(AuditLog), typeof(RefreshToken), typeof(UserToken), typeof(Domain.Catalog.StoredFile),
        typeof(Domain.Inventory.InventoryTransaction), typeof(Domain.Inventory.WarehouseStock), typeof(IdempotencyRecord),
        // Notifications are messages about changes that are already audited; logging them (and every "read") is noise.
        typeof(Domain.Notifications.Notification), typeof(Domain.Notifications.NotificationPreference),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditableEntity)
            {
                Stamp(entry, now, userId);
            }

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                !NotAudited.Contains(entry.Metadata.ClrType) &&
                BuildChanges(entry) is { } changes)
            {
                logs.Add(new AuditLog(
                    ResolveTenantId(entry.Entity), userId, entry.State switch
                    {
                        EntityState.Added => "Created",
                        EntityState.Deleted => "Deleted",
                        _ => "Updated",
                    },
                    entry.Metadata.ClrType.Name, PrimaryKey(entry), changes,
                    currentUser.IpAddress, currentUser.CorrelationId, now));
            }
        }

        context.Set<AuditLog>().AddRange(logs);
    }

    private static void Stamp(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(AuditableEntity.CreatedBy)).CurrentValue = userId;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(AuditableEntity.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(AuditableEntity.UpdatedBy)).CurrentValue = userId;
        }
    }

    private static string? BuildChanges(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (ExcludedProperties.Contains(name) || property.Metadata.IsShadowProperty() || property.Metadata.IsConcurrencyToken)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    changes[name] = new { @new = property.CurrentValue };
                    break;
                case EntityState.Deleted:
                    changes[name] = new { old = property.OriginalValue };
                    break;
                case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    changes[name] = new { old = property.OriginalValue, @new = property.CurrentValue };
                    break;
            }
        }

        // An update that only touched excluded fields (e.g. LastLoginAt) isn't worth a log row.
        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes);
    }

    private Guid? ResolveTenantId(object entity) => entity switch
    {
        ITenantOwned owned => owned.TenantId,
        User user => user.TenantId,
        Tenant tenant => tenant.Id,
        Role role => role.TenantId,
        _ => currentUser.TenantId,
    };

    private static string? PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? null
            : string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }
}
