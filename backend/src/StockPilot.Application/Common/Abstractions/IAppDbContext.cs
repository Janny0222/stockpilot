using Microsoft.EntityFrameworkCore;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Identity;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
