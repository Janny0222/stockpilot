using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Purchasing;
using StockPilot.Domain.Sales;
using StockPilot.Domain.Tenancy;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IAppDbContext
{
    /// <summary>
    /// Referenced by every tenant query filter. EF evaluates this per query (it becomes a SQL parameter),
    /// so one compiled model serves every tenant safely.
    /// </summary>
    public Guid? CurrentTenantId => currentUser.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<UserWarehouseAccess> UserWarehouseAccess => Set<UserWarehouseAccess>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<SalesShipment> SalesShipments => Set<SalesShipment>();
    public DbSet<SalesShipmentItem> SalesShipmentItems => Set<SalesShipmentItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        // With EnableRetryOnFailure, a user-started transaction must run INSIDE the execution strategy,
        // so a retry replays the whole thing (begin → work → commit), not just the failed statement.
        var strategy = Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async ct =>
        {
            ChangeTracker.Clear();
            await using var transaction = await Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    private static readonly Dictionary<string, (string Code, string Message)> UniqueConstraintMessages = new()
    {
        ["ix_users_email"] = ("EMAIL_TAKEN", "An account with this email already exists."),
        ["ix_tenants_slug"] = ("SLUG_TAKEN", "A company with this identifier already exists."),
        ["ix_warehouses_tenant_id_code"] = ("WAREHOUSE_CODE_TAKEN", "A warehouse with this code already exists."),
        ["ix_product_variants_tenant_id_sku"] = ("SKU_TAKEN", "Another product already uses this SKU."),
        ["ix_product_variants_tenant_id_barcode"] = ("BARCODE_TAKEN", "Another product already uses this barcode."),
        ["ix_product_categories_tenant_id_parent_id_name"] = ("CATEGORY_NAME_TAKEN", "A category with this name already exists here."),
        ["ix_suppliers_tenant_id_code"] = ("SUPPLIER_CODE_TAKEN", "Another supplier already uses this code."),
        ["ix_customers_tenant_id_code"] = ("CUSTOMER_CODE_TAKEN", "Another customer already uses this code."),
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyClientGeneratedKeys(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// IDs are UUID v7 created in C# (Entity.Id), not by the database. Telling EF that matters: by default
    /// EF assumes an entity that already has a key value is an EXISTING row, so a new child added to a
    /// loaded parent (product.AddImage, product.AddVariant) would be tracked as Modified and saved as an
    /// UPDATE of a row that doesn't exist. ValueGeneratedNever = "a key I haven't seen means a new row".
    /// </summary>
    private static void ApplyClientGeneratedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // Dependents (refresh tokens, email tokens) of the tenant-filtered User are only ever loaded by
        // explicit user-scoped queries, so EF's "required navigation + query filter" warning is expected.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // A unique index caught a race the application-level check missed. Turn it into a clean 409.
            var (code, message) = pg.ConstraintName is { } name && UniqueConstraintMessages.TryGetValue(name, out var known)
                ? known
                : ("DUPLICATE", "A record with the same unique value already exists.");
            throw new ConflictException(code, message);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row's xmin no longer matches the version the client loaded: someone else saved first.
            throw new ConflictException("CONCURRENCY_CONFLICT", "This record was changed by someone else. Reload it and try again.");
        }
    }

    /// <summary>
    /// Adds "WHERE tenant_id = @current" to every entity implementing ITenantOwned, by convention.
    /// A new tenant-owned entity is protected the moment it implements the interface; nobody has
    /// to remember to add a filter. When there is no tenant (anonymous, platform admin) the filter
    /// matches NOTHING, so the safe default is deny.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
            var entityTenant = Expression.Convert(Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?));

            var body = Expression.AndAlso(
                Expression.NotEqual(current, Expression.Constant(null, typeof(Guid?))),
                Expression.Equal(entityTenant, current));

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, entity));
        }

        // Users and audit logs have a NULLABLE TenantId (platform admin / platform events), so they
        // can't implement ITenantOwned. Same rule, written by hand.
        modelBuilder.Entity<User>().HasQueryFilter(u => CurrentTenantId != null && u.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => CurrentTenantId != null && a.TenantId == CurrentTenantId);
    }
}
