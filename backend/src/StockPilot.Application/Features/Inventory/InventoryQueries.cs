using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Common.Security;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;

namespace StockPilot.Application.Features.Inventory;

public enum StockStatus
{
    InStock = 1,
    Low = 2,
    Out = 3,
}

public sealed record StockLevelDto(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid ProductId,
    string ProductName,
    Guid VariantId,
    string Sku,
    string? VariantName,
    string UnitOfMeasure,
    decimal OnHand,
    decimal Reserved,
    decimal Available,
    decimal ReorderLevel,
    StockStatus Status,
    decimal? AverageUnitCost,
    decimal? StockValue);

public sealed record ListStockQuery : PageRequest
{
    public Guid? WarehouseId { get; init; }
    public Guid? CategoryId { get; init; }
    public bool LowStockOnly { get; init; }
}

public sealed class ListStockHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions)
{
    public async Task<PagedResult<StockLevelDto>> Handle(ListStockQuery query, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        var rows =
            from s in db.WarehouseStocks.AsNoTracking()
            join w in db.Warehouses on s.WarehouseId equals w.Id
            join v in db.ProductVariants on s.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where p.Status == ProductStatus.Active && v.IsActive && w.IsActive
            select new { s, w, v, p };

        if (allowed is not null)
        {
            rows = rows.Where(r => allowed.Contains(r.w.Id));
        }

        if (query.WarehouseId is { } warehouseId)
        {
            rows = rows.Where(r => r.w.Id == warehouseId);
        }

        if (query.CategoryId is { } categoryId)
        {
            rows = rows.Where(r => r.p.CategoryId == categoryId || r.p.Category!.ParentId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.p.Name.ToLower().Contains(term) || r.v.Sku.ToLower().Contains(term)
                || (r.v.Barcode != null && r.v.Barcode.ToLower().Contains(term)));
        }

        if (query.LowStockOnly)
        {
            rows = rows.Where(r => r.s.QuantityOnHand - r.s.QuantityReserved <= r.v.ReorderLevel);
        }

        rows = query.ParseSort() switch
        {
            ("available", false) => rows.OrderBy(r => r.s.QuantityOnHand - r.s.QuantityReserved).ThenBy(r => r.p.Name),
            ("available", true) => rows.OrderByDescending(r => r.s.QuantityOnHand - r.s.QuantityReserved).ThenBy(r => r.p.Name),
            // Sorting by value would reveal the cost ranking even with the value column hidden, so only cost viewers get it.
            ("value", true) when canViewCost => rows.OrderByDescending(r => r.s.QuantityOnHand * r.s.AverageUnitCost),
            ("value", false) when canViewCost => rows.OrderBy(r => r.s.QuantityOnHand * r.s.AverageUnitCost),
            ("name", true) => rows.OrderByDescending(r => r.p.Name).ThenBy(r => r.v.Sku),
            _ => rows.OrderBy(r => r.p.Name).ThenBy(r => r.v.Sku).ThenBy(r => r.w.Code),
        };

        return await rows
            .Select(r => new StockLevelDto(
                r.w.Id, r.w.Code, r.w.Name, r.p.Id, r.p.Name, r.v.Id, r.v.Sku, r.v.Name, r.p.UnitOfMeasure,
                r.s.QuantityOnHand,
                r.s.QuantityReserved,
                r.s.QuantityOnHand - r.s.QuantityReserved,
                r.v.ReorderLevel,
                r.s.QuantityOnHand - r.s.QuantityReserved <= 0 ? StockStatus.Out
                    : r.s.QuantityOnHand - r.s.QuantityReserved <= r.v.ReorderLevel ? StockStatus.Low
                    : StockStatus.InStock,
                canViewCost ? r.s.AverageUnitCost : null,
                canViewCost ? decimal.Round(r.s.QuantityOnHand * r.s.AverageUnitCost, 2) : null))
            .ToPagedResultAsync(query, ct);
    }
}

public sealed record MovementDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    InventoryMovementType Type,
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    string ProductName,
    string Sku,
    string? VariantName,
    decimal QuantityChange,
    decimal QuantityAfter,
    decimal? UnitCost,
    string? Reference,
    string? Note,
    AdjustmentReason? Reason,
    string? PerformedBy,
    string? SourceType,
    Guid? SourceId);

public sealed record ListMovementsQuery : PageRequest
{
    public Guid? WarehouseId { get; init; }
    public Guid? VariantId { get; init; }
    public Guid? ProductId { get; init; }
    public InventoryMovementType? Type { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

/// <summary>The ledger, newest first. Read-only by nature: nothing in the API can edit or delete a movement.</summary>
public sealed class ListMovementsHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions)
{
    public async Task<PagedResult<MovementDto>> Handle(ListMovementsQuery query, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        // Step 1: filter the ledger ITSELF. The page count (COUNT(*)) then runs on the ledger's own indexes, instead of
        // over a four-way join on every row (performance pass: 300k rows, 134 ms → see docs/15-performance.md).
        var ledger = db.InventoryTransactions.AsNoTracking();
        if (allowed is not null) ledger = ledger.Where(t => allowed.Contains(t.WarehouseId));
        if (query.WarehouseId is { } warehouseId) ledger = ledger.Where(t => t.WarehouseId == warehouseId);
        if (query.VariantId is { } variantId) ledger = ledger.Where(t => t.ProductVariantId == variantId);
        if (query.ProductId is { } productId) ledger = ledger.Where(t => db.ProductVariants.Any(v => v.Id == t.ProductVariantId && v.ProductId == productId));
        if (query.Type is { } type) ledger = ledger.Where(t => t.Type == type);
        if (query.From is { } from) ledger = ledger.Where(t => t.OccurredAt >= from);
        if (query.To is { } to) ledger = ledger.Where(t => t.OccurredAt < to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            ledger = ledger.Where(t =>
                (t.Reference != null && t.Reference.ToLower().Contains(term)) ||
                db.ProductVariants.Any(v => v.Id == t.ProductVariantId && (v.Sku.ToLower().Contains(term)
                    || db.Products.Any(p => p.Id == v.ProductId && p.Name.ToLower().Contains(term)))));
        }

        var total = await ledger.CountAsync(ct);

        // Step 2: only this page's rows get joined to names.
        var items = await (
            from t in ledger.OrderByDescending(t => t.OccurredAt).ThenByDescending(t => t.Id)
                .Skip((query.SafePage - 1) * query.SafePageSize).Take(query.SafePageSize)
            join w in db.Warehouses on t.WarehouseId equals w.Id
            join v in db.ProductVariants on t.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            join u in db.Users on t.PerformedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            orderby t.OccurredAt descending, t.Id descending
            select new MovementDto(
                t.Id, t.OccurredAt, t.Type, w.Id, w.Code, p.Id, p.Name, v.Sku, v.Name,
                t.QuantityChange, t.QuantityAfter, canViewCost ? t.UnitCost : null,
                t.Reference, t.Note, t.Reason,
                u != null ? u.FirstName + " " + u.LastName : null,
                t.SourceType, t.SourceId))
            .ToListAsync(ct);

        return new PagedResult<MovementDto>(items, query.SafePage, query.SafePageSize, total);
    }
}

public sealed record TransferListItemDto(
    Guid Id, string Number, Guid SourceWarehouseId, string SourceWarehouseCode, Guid DestinationWarehouseId,
    string DestinationWarehouseCode, int ItemCount, decimal TotalQuantity, string? Note, DateTimeOffset CompletedAt, string? CompletedBy);

public sealed record TransferItemDto(Guid VariantId, Guid ProductId, string ProductName, string Sku, string? VariantName, string UnitOfMeasure, decimal Quantity, decimal? UnitCost);

public sealed record TransferDetailDto(
    Guid Id, string Number, Guid SourceWarehouseId, string SourceWarehouseName, Guid DestinationWarehouseId, string DestinationWarehouseName,
    string? Note, DateTimeOffset CompletedAt, string? CompletedBy, IReadOnlyList<TransferItemDto> Items);

public sealed class ListTransfersHandler(IAppDbContext db, WarehouseAccessService access)
{
    public async Task<PagedResult<TransferListItemDto>> Handle(PageRequest query, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);

        var rows =
            from t in db.StockTransfers.AsNoTracking()
            join src in db.Warehouses on t.SourceWarehouseId equals src.Id
            join dst in db.Warehouses on t.DestinationWarehouseId equals dst.Id
            join u in db.Users on t.CompletedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            select new { t, src, dst, u };

        if (allowed is not null)
        {
            rows = rows.Where(r => allowed.Contains(r.src.Id) || allowed.Contains(r.dst.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            rows = rows.Where(r => r.t.Number.Contains(term));
        }

        return await rows
            .OrderByDescending(r => r.t.CompletedAt)
            .Select(r => new TransferListItemDto(
                r.t.Id, r.t.Number, r.src.Id, r.src.Code, r.dst.Id, r.dst.Code,
                r.t.Items.Count, r.t.Items.Sum(i => i.Quantity), r.t.Note, r.t.CompletedAt,
                r.u != null ? r.u.FirstName + " " + r.u.LastName : null))
            .ToPagedResultAsync(query, ct);
    }
}

public sealed class GetTransferHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions)
{
    public async Task<TransferDetailDto> Handle(Guid id, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        var header = await (
            from t in db.StockTransfers.AsNoTracking()
            join src in db.Warehouses on t.SourceWarehouseId equals src.Id
            join dst in db.Warehouses on t.DestinationWarehouseId equals dst.Id
            join u in db.Users on t.CompletedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            where t.Id == id && (allowed == null || allowed.Contains(src.Id) || allowed.Contains(dst.Id))
            select new { t.Id, t.Number, SrcId = src.Id, SrcName = src.Name, DstId = dst.Id, DstName = dst.Name, t.Note, t.CompletedAt, By = u != null ? u.FirstName + " " + u.LastName : null })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Transfer", id);

        var items = await (
            from i in db.StockTransferItems.AsNoTracking()
            join v in db.ProductVariants on i.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where i.StockTransferId == id
            orderby p.Name, v.Sku
            select new TransferItemDto(v.Id, p.Id, p.Name, v.Sku, v.Name, p.UnitOfMeasure, i.Quantity, canViewCost ? i.UnitCost : null))
            .ToListAsync(ct);

        return new TransferDetailDto(header.Id, header.Number, header.SrcId, header.SrcName, header.DstId, header.DstName, header.Note, header.CompletedAt, header.By, items);
    }
}

public sealed record VariantOptionDto(
    Guid VariantId, Guid ProductId, string ProductName, string Sku, string? VariantName, string UnitOfMeasure,
    decimal? CostPrice, decimal OnHand, decimal Available, decimal SellingPrice);

/// <summary>Type-ahead for stock forms: active variants matching name/SKU/barcode, with stock in the chosen warehouse.</summary>
public sealed class SearchStockVariantsHandler(IAppDbContext db, PermissionChecker permissions, WarehouseAccessService access)
{
    public async Task<IReadOnlyList<VariantOptionDto>> Handle(string? search, Guid? warehouseId, CancellationToken ct)
    {
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        // The quantities come from the requested warehouse, so it must be one this user may see. Without this check an
        // Inventory Staff member could read any warehouse's stock by passing its ID (found in a security review).
        if (warehouseId is { } requested && await access.GetAccessibleWarehouseIdsAsync(ct) is { } allowed && !allowed.Contains(requested))
        {
            throw new NotFoundException("Warehouse", requested);
        }

        var rows =
            from v in db.ProductVariants.AsNoTracking()
            join p in db.Products on v.ProductId equals p.Id
            where v.IsActive && p.Status == ProductStatus.Active
            select new { v, p };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.p.Name.ToLower().Contains(term) || r.v.Sku.ToLower().Contains(term)
                || (r.v.Barcode != null && r.v.Barcode == search.Trim()));
        }

        return await rows
            .OrderBy(r => r.p.Name).ThenBy(r => r.v.Sku)
            .Take(20)
            .Select(r => new VariantOptionDto(
                r.v.Id, r.p.Id, r.p.Name, r.v.Sku, r.v.Name, r.p.UnitOfMeasure,
                canViewCost ? r.v.CostPrice : null,
                db.WarehouseStocks.Where(s => s.ProductVariantId == r.v.Id && s.WarehouseId == warehouseId).Select(s => s.QuantityOnHand).FirstOrDefault(),
                db.WarehouseStocks.Where(s => s.ProductVariantId == r.v.Id && s.WarehouseId == warehouseId).Select(s => s.QuantityOnHand - s.QuantityReserved).FirstOrDefault(),
                r.v.SellingPrice))
            .ToListAsync(ct);
    }
}
