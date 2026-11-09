using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;

namespace StockPilot.Application.Features.Auth;

public sealed record MeTenantDto(Guid Id, string Name, string Slug, string Currency, string TimeZone);

public sealed record MeRoleDto(Guid Id, string Name);

public sealed record WarehouseScopeDto(bool Restricted, IReadOnlyList<Guid> WarehouseIds);

/// <summary>
/// Everything the SPA needs after sign-in. <see cref="Permissions"/> drives navigation and button
/// visibility in the UI, purely as UX. Every endpoint re-checks permissions server-side.
/// </summary>
public sealed record MeResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsPlatformAdmin,
    MeRoleDto Role,
    IReadOnlyList<string> Permissions,
    MeTenantDto? Tenant,
    WarehouseScopeDto WarehouseScope);

public sealed class GetCurrentUserHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<MeResponse> Handle(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("UNAUTHENTICATED", "Not signed in.");

        // The ID comes from the validated token, so bypassing the tenant filter is safe here; it is
        // needed because the platform Super Admin has no tenant.
        var user = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id, u.Email, u.FirstName, u.LastName, u.TenantId, u.RoleId,
                RoleName = u.Role.Name,
                u.Role.RestrictToAssignedWarehouses,
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("User", userId);

        MeTenantDto? tenant = null;
        if (user.TenantId is { } tenantId)
        {
            tenant = await db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => new MeTenantDto(t.Id, t.Name, t.Slug, t.Currency, t.TimeZone))
                .SingleAsync(ct);
        }

        var warehouseIds = user.RestrictToAssignedWarehouses
            ? await db.UserWarehouseAccess.Where(a => a.UserId == userId).Select(a => a.WarehouseId).ToListAsync(ct)
            : [];

        var permissionSet = await permissions.GetPermissionsAsync(user.RoleId, ct);

        return new MeResponse(
            user.Id, user.Email, user.FirstName, user.LastName,
            IsPlatformAdmin: user.TenantId is null,
            new MeRoleDto(user.RoleId, user.RoleName),
            permissionSet.Order(StringComparer.Ordinal).ToList(),
            tenant,
            new WarehouseScopeDto(user.RestrictToAssignedWarehouses, warehouseIds));
    }
}
