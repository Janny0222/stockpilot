using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Notifications;
using StockPilot.Domain.Purchasing;
using StockPilot.Domain.Sales;
using StockPilot.Domain.Tenancy;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Application.Common.Abstractions;

/// <summary>
/// The persistence port used by use cases. DbContext already IS a unit of work and each DbSet
/// already IS a repository, so we expose them directly instead of wrapping every entity in an
/// IXxxRepository (see Phase 1 plan §1.2). Tenant query filters are applied by the implementation,
/// so handlers get tenant isolation for free.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserToken> UserTokens { get; }
    DbSet<UserWarehouseAccess> UserWarehouseAccess { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<WarehouseStock> WarehouseStocks { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferItem> StockTransferItems { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<PurchaseReceipt> PurchaseReceipts { get; }
    DbSet<PurchaseReceiptItem> PurchaseReceiptItems { get; }
    DbSet<Customer> Customers { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesOrderItem> SalesOrderItems { get; }
    DbSet<SalesShipment> SalesShipments { get; }
    DbSet<SalesShipmentItem> SalesShipmentItems { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<Domain.Platform.JobRun> JobRuns { get; }

    /// <summary>
    /// Runs <paramref name="work"/> inside ONE database transaction, retrying the WHOLE unit of work on transient
    /// failures (deadlock, serialization failure, dropped connection). Each attempt starts with an empty change
    /// tracker, so a retry never sees half-applied state from the failed attempt.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>Change-tracking entry, used to apply a client's concurrency token before saving.</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    /// Saves all changes in one transaction. Translates database conflicts into application exceptions:
    /// unique-index violations → ConflictException(code), stale concurrency tokens → ConflictException("CONCURRENCY_CONFLICT").
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
