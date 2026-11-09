using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
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

    private static readonly Dictionary<string, (string Code, string Message)> UniqueConstraintMessages = new()
    {
        ["ix_users_email"] = ("EMAIL_TAKEN", "An account with this email already exists."),
        ["ix_tenants_slug"] = ("SLUG_TAKEN", "A company with this identifier already exists."),
        ["ix_warehouses_tenant_id_code"] = ("WAREHOUSE_CODE_TAKEN", "A warehouse with this code already exists."),
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTenantQueryFilters(modelBuilder);
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
