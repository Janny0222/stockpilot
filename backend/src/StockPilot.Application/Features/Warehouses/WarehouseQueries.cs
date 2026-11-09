using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Application.Features.Warehouses;

public sealed record WarehouseDto(
    Guid Id, string Code, string Name, string? AddressLine, string? City, string? Country, bool IsDefault, bool IsActive);

public sealed record ListWarehousesQuery : PageRequest
{
    public bool? IsActive { get; init; }
}

/// <summary>
/// Resolves which warehouses the current user may see. Returns null for "all warehouses"
/// (roles not restricted to assigned warehouses, e.g. Owner/Manager).
/// </summary>
public sealed class WarehouseAccessService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<IReadOnlyCollection<Guid>?> GetAccessibleWarehouseIdsAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("UNAUTHENTICATED", "Not signed in.");

        var restricted = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Role.RestrictToAssignedWarehouses)
            .SingleOrDefaultAsync(ct);

        if (!restricted)
        {
            return null;
        }

        return await db.UserWarehouseAccess.Where(a => a.UserId == userId).Select(a => a.WarehouseId).ToListAsync(ct);
    }
}

/// <summary>
/// Note what is NOT here: no "WHERE tenant_id = …". The global query filter adds it to every query,
/// so a handler cannot forget it. Integration tests prove it.
/// </summary>
public sealed class ListWarehousesHandler(IAppDbContext db, WarehouseAccessService access)
{
    public async Task<PagedResult<WarehouseDto>> Handle(ListWarehousesQuery query, CancellationToken ct)
    {
        IQueryable<Warehouse> warehouses = db.Warehouses.AsNoTracking();

        if (await access.GetAccessibleWarehouseIdsAsync(ct) is { } allowed)
        {
            warehouses = warehouses.Where(w => allowed.Contains(w.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Translates to LOWER(name) LIKE '%' || @term || '%' with @term as a bound parameter (no SQL injection).
            var term = query.Search.Trim().ToLowerInvariant();
            warehouses = warehouses.Where(w => w.Name.ToLower().Contains(term) || w.Code.ToLower().Contains(term));
        }

        if (query.IsActive is { } isActive)
        {
            warehouses = warehouses.Where(w => w.IsActive == isActive);
        }

        warehouses = query.ParseSort() switch
        {
            ("code", false) => warehouses.OrderBy(w => w.Code),
            ("code", true) => warehouses.OrderByDescending(w => w.Code),
            ("name", true) => warehouses.OrderByDescending(w => w.Name),
            ("createdat", false) => warehouses.OrderBy(w => w.CreatedAt),
            ("createdat", true) => warehouses.OrderByDescending(w => w.CreatedAt),
            _ => warehouses.OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name),
        };

        return await warehouses.Select(ToDto).ToPagedResultAsync(query, ct);
    }

    internal static readonly System.Linq.Expressions.Expression<Func<Warehouse, WarehouseDto>> ToDto = w =>
        new WarehouseDto(w.Id, w.Code, w.Name, w.AddressLine, w.City, w.Country, w.IsDefault, w.IsActive);
}

public sealed class GetWarehouseHandler(IAppDbContext db, WarehouseAccessService access)
{
    public async Task<WarehouseDto> Handle(Guid id, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);

        // Another tenant's ID is filtered out by the query filter → 404 (not 403), so we never confirm it exists.
        var dto = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == id)
            .Where(w => allowed == null || allowed.Contains(w.Id))
            .Select(ListWarehousesHandler.ToDto)
            .SingleOrDefaultAsync(ct);

        return dto ?? throw new NotFoundException("Warehouse", id);
    }
}
