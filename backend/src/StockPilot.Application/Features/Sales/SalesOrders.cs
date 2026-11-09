using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Common.Security;
using StockPilot.Application.Features.Inventory;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Sales;

namespace StockPilot.Application.Features.Sales;

// ======================= Contracts =======================

public sealed record SalesLineInput(Guid VariantId, decimal Quantity, decimal UnitPrice);

public sealed record SaveSalesOrderCommand(
    Guid CustomerId, Guid WarehouseId, DateOnly OrderDate, string? Notes, IReadOnlyList<SalesLineInput> Lines, uint? Version);

public sealed record CancelSalesOrderCommand(string Reason);

public sealed record ShipLineInput(Guid ItemId, decimal Quantity);

public sealed record ShipSalesOrderCommand(IReadOnlyList<ShipLineInput> Lines, string? Note);

public sealed record ShipmentResult(Guid ShipmentId, string ShipmentNumber, SalesOrderStatus Status);

public sealed record SalesOrderListItemDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, Guid WarehouseId, string WarehouseCode,
    SalesOrderStatus Status, DateOnly OrderDate, int ItemCount, decimal QuantityOrdered, decimal QuantityShipped, decimal Total);

public sealed record SalesOrderItemDto(
    Guid Id, Guid VariantId, Guid ProductId, string ProductName, string Sku, string? VariantName, string UnitOfMeasure,
    decimal QuantityOrdered, decimal QuantityShipped, decimal QuantityRemaining, decimal UnitPrice, decimal LineTotal,
    decimal Available);

public sealed record ShipmentItemDto(string Sku, string ProductName, decimal Quantity);

public sealed record ShipmentDto(Guid Id, string Number, DateTimeOffset ShippedAt, string? ShippedBy, string? Note, IReadOnlyList<ShipmentItemDto> Items);

/// <summary>Revenue, cost and margin of what has SHIPPED so far. Null unless the user may see costs.</summary>
public sealed record SalesProfitDto(decimal Revenue, decimal CostOfGoods, decimal GrossProfit, decimal? MarginPercent);

public sealed record SalesOrderDetailDto(
    Guid Id, string Number, SalesOrderStatus Status, Guid CustomerId, string CustomerName, Guid WarehouseId, string WarehouseName,
    DateOnly OrderDate, string? Notes, decimal Total, uint Version,
    string? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt, string? ConfirmedBy,
    DateTimeOffset? CancelledAt, string? CancelReason, SalesProfitDto? Profit,
    IReadOnlyList<SalesOrderItemDto> Items, IReadOnlyList<ShipmentDto> Shipments);

public sealed record ListSalesOrdersQuery : PageRequest
{
    public SalesOrderStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? WarehouseId { get; init; }
}

// ======================= Validation =======================

public sealed class SaveSalesOrderValidator : AbstractValidator<SaveSalesOrderCommand>
{
    public SaveSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Choose a customer.");
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Choose the warehouse to ship from.");
        RuleFor(x => x.Notes).MaximumLength(SalesOrder.NotesMaxLength);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one item.")
            .Must(l => l.Count <= SalesOrder.MaxItems).WithMessage($"Up to {SalesOrder.MaxItems} items per order.")
            .Must(l => l.Select(x => x.VariantId).Distinct().Count() == l.Count).WithMessage("Each item can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).ValidQuantity();
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000_000);
        });
    }
}

public sealed class ShipSalesOrderValidator : AbstractValidator<ShipSalesOrderCommand>
{
    public ShipSalesOrderValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Enter what is being shipped.")
            .Must(l => l.Any(x => x.Quantity > 0)).WithMessage("Enter a quantity for at least one item.")
            .Must(l => l.Select(x => x.ItemId).Distinct().Count() == l.Count).WithMessage("Each line can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
            line.RuleFor(l => l.Quantity).GreaterThanOrEqualTo(0).LessThan(1_000_000_000)
                .Must(q => decimal.Round(q, 3) == q).WithMessage("Use at most 3 decimal places."));
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

public sealed class CancelSalesOrderValidator : AbstractValidator<CancelSalesOrderCommand>
{
    public CancelSalesOrderValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason for cancelling.").MaximumLength(300);
}

// ======================= Queries =======================

public sealed class ListSalesOrdersHandler(IAppDbContext db, WarehouseAccessService access)
{
    public async Task<PagedResult<SalesOrderListItemDto>> Handle(ListSalesOrdersQuery query, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);

        var rows =
            from o in db.SalesOrders.AsNoTracking()
            join c in db.Customers on o.CustomerId equals c.Id
            join w in db.Warehouses on o.WarehouseId equals w.Id
            select new { o, c, w };

        if (allowed is not null) rows = rows.Where(r => allowed.Contains(r.w.Id));
        if (query.Status is { } status) rows = rows.Where(r => r.o.Status == status);
        if (query.CustomerId is { } customerId) rows = rows.Where(r => r.o.CustomerId == customerId);
        if (query.WarehouseId is { } warehouseId) rows = rows.Where(r => r.o.WarehouseId == warehouseId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.o.Number.ToLower().Contains(term) || r.c.Name.ToLower().Contains(term));
        }

        rows = query.ParseSort() switch
        {
            ("total", true) => rows.OrderByDescending(r => r.o.Total),
            ("total", false) => rows.OrderBy(r => r.o.Total),
            ("date", false) => rows.OrderBy(r => r.o.OrderDate).ThenBy(r => r.o.Number),
            ("date", true) => rows.OrderByDescending(r => r.o.OrderDate).ThenByDescending(r => r.o.Number),
            ("number", false) => rows.OrderBy(r => r.o.Number),
            _ => rows.OrderByDescending(r => r.o.Number),
        };

        return await rows
            .Select(r => new SalesOrderListItemDto(
                r.o.Id, r.o.Number, r.c.Id, r.c.Name, r.w.Id, r.w.Code, r.o.Status, r.o.OrderDate,
                r.o.Items.Count, r.o.Items.Sum(i => i.QuantityOrdered), r.o.Items.Sum(i => i.QuantityShipped), r.o.Total))
            .ToPagedResultAsync(query, ct);
    }
}

public sealed class GetSalesOrderHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions)
{
    public async Task<SalesOrderDetailDto> Handle(Guid id, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        var header = await (
            from o in db.SalesOrders.AsNoTracking()
            join c in db.Customers on o.CustomerId equals c.Id
            join w in db.Warehouses on o.WarehouseId equals w.Id
            join cu in db.Users on o.CreatedBy equals cu.Id into creators
            from cu in creators.DefaultIfEmpty()
            join fu in db.Users on o.ConfirmedBy equals fu.Id into confirmers
            from fu in confirmers.DefaultIfEmpty()
            where o.Id == id && (allowed == null || allowed.Contains(w.Id))
            select new
            {
                o, CustomerName = c.Name, WarehouseName = w.Name,
                CreatedBy = cu != null ? cu.FirstName + " " + cu.LastName : null,
                ConfirmedBy = fu != null ? fu.FirstName + " " + fu.LastName : null,
            }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Sales order", id);

        var order = header.o;
        var items = await (
            from i in db.SalesOrderItems.AsNoTracking()
            join v in db.ProductVariants on i.ProductVariantId equals v.Id
            join pr in db.Products on v.ProductId equals pr.Id
            where i.SalesOrderId == id
            orderby pr.Name, v.Sku
            select new SalesOrderItemDto(
                i.Id, v.Id, pr.Id, pr.Name, v.Sku, v.Name, pr.UnitOfMeasure, i.QuantityOrdered, i.QuantityShipped,
                i.QuantityOrdered - i.QuantityShipped, i.UnitPrice, i.LineTotal,
                // What the warehouse could still promise right now (helps decide whether a draft can be confirmed).
                db.WarehouseStocks.Where(s => s.WarehouseId == order.WarehouseId && s.ProductVariantId == v.Id)
                    .Select(s => s.QuantityOnHand - s.QuantityReserved).FirstOrDefault()))
            .ToListAsync(ct);

        var shipmentRows = await (
            from s in db.SalesShipments.AsNoTracking()
            join u in db.Users on s.ShippedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            where s.SalesOrderId == id
            orderby s.ShippedAt
            select new { s.Id, s.Number, s.ShippedAt, By = u != null ? u.FirstName + " " + u.LastName : null, s.Note })
            .ToListAsync(ct);

        var shipmentIds = shipmentRows.Select(s => s.Id).ToList();
        var shipmentItems = await (
            from si in db.SalesShipmentItems.AsNoTracking()
            join v in db.ProductVariants on si.ProductVariantId equals v.Id
            join pr in db.Products on v.ProductId equals pr.Id
            where shipmentIds.Contains(si.SalesShipmentId)
            select new { si.SalesShipmentId, v.Sku, pr.Name, si.Quantity, si.UnitPrice, si.UnitCost })
            .ToListAsync(ct);

        var shipments = shipmentRows.Select(s => new ShipmentDto(s.Id, s.Number, s.ShippedAt, s.By, s.Note,
            shipmentItems.Where(i => i.SalesShipmentId == s.Id).Select(i => new ShipmentItemDto(i.Sku, i.Name, i.Quantity)).ToList())).ToList();

        SalesProfitDto? profit = null;
        if (canViewCost)
        {
            var revenue = decimal.Round(shipmentItems.Sum(i => i.Quantity * i.UnitPrice), 2);
            var cost = decimal.Round(shipmentItems.Sum(i => i.Quantity * i.UnitCost), 2);
            profit = new SalesProfitDto(revenue, cost, revenue - cost, revenue == 0 ? null : decimal.Round((revenue - cost) / revenue * 100, 1));
        }

        return new SalesOrderDetailDto(
            order.Id, order.Number, order.Status, order.CustomerId, header.CustomerName, order.WarehouseId, header.WarehouseName,
            order.OrderDate, order.Notes, order.Total, order.Version,
            header.CreatedBy, order.CreatedAt, order.ConfirmedAt, header.ConfirmedBy, order.CancelledAt, order.CancelReason, profit,
            items, shipments);
    }
}

// ======================= Commands =======================

internal static class SalesRules
{
    public static async Task EnsureCustomerAndWarehouseAsync(IAppDbContext db, Guid customerId, Guid warehouseId, CancellationToken ct)
    {
        // Tenant query filters make another company's IDs look exactly like missing ones (IDOR-safe).
        if (!await db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive, ct))
        {
            throw new DomainException("CUSTOMER_NOT_FOUND", "Choose an active customer.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.IsActive, ct))
        {
            throw new DomainException("WAREHOUSE_NOT_FOUND", "Choose an active warehouse.");
        }
    }

    public static async Task<SalesOrder> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.SalesOrders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException("Sales order", id);

    public static async Task<Guid> WarehouseOfAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.SalesOrders.Where(o => o.Id == id).Select(o => (Guid?)o.WarehouseId).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Sales order", id);

    public static Task<Dictionary<Guid, string>> SkusAsync(IAppDbContext db, IEnumerable<Guid> variantIds, CancellationToken ct)
    {
        var ids = variantIds.ToList();
        return db.ProductVariants.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Sku, ct);
    }
}

public sealed class CreateSalesOrderHandler(
    IAppDbContext db, IValidator<SaveSalesOrderCommand> validator, StockOperations ops, IDocumentNumberGenerator numbers)
{
    public async Task<Guid> Handle(SaveSalesOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        await SalesRules.EnsureCustomerAndWarehouseAsync(db, c.CustomerId, c.WarehouseId, ct);
        await ops.EnsureVariantsUsableAsync(c.Lines.Select(l => l.VariantId).ToList(), ct);

        // Number + insert in one transaction: a failed save never burns an SO number.
        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var number = await numbers.NextAsync(ops.TenantId, "SO", tx);
            var order = new SalesOrder(ops.TenantId, number, c.CustomerId, c.WarehouseId, c.OrderDate, c.Notes);
            order.ReplaceItems(c.Lines.Select(l => (l.VariantId, l.Quantity, l.UnitPrice)));
            db.SalesOrders.Add(order);
            await db.SaveChangesAsync(tx);
            return order.Id;
        }, ct);
    }
}

public sealed class UpdateSalesOrderHandler(IAppDbContext db, IValidator<SaveSalesOrderCommand> validator, StockOperations ops)
{
    public async Task Handle(Guid id, SaveSalesOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        await SalesRules.EnsureCustomerAndWarehouseAsync(db, c.CustomerId, c.WarehouseId, ct);
        await ops.EnsureVariantsUsableAsync(c.Lines.Select(l => l.VariantId).ToList(), ct);

        var order = await SalesRules.LoadAsync(db, id, ct);
        if (c.Version is { } version)
        {
            db.Entry(order).Property(o => o.Version).OriginalValue = version; // stale edit → 409
        }

        order.SetHeader(c.CustomerId, c.WarehouseId, c.OrderDate, c.Notes);
        order.ReplaceItems(c.Lines.Select(l => (l.VariantId, l.Quantity, l.UnitPrice)));
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Confirming = promising the stock. Inside ONE transaction: lock every stock row of the order (id order, same
/// engine as stock-out), check AVAILABLE for each line, add the reservation. If any line is short, nothing is
/// reserved and the order stays a draft. Two orders racing for the last units queue on the row lock; the second
/// sees the first one's reservation and fails cleanly with INSUFFICIENT_STOCK.
/// </summary>
public sealed class ConfirmSalesOrderHandler(IAppDbContext db, StockOperations ops, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task Handle(Guid id, CancellationToken ct)
    {
        await ops.EnsureWarehouseUsableAsync(await SalesRules.WarehouseOfAsync(db, id, ct), ct);
        await ops.EnsureVariantsUsableAsync(
            await db.SalesOrderItems.Where(i => i.SalesOrderId == id).Select(i => i.ProductVariantId).ToListAsync(ct), ct);
        var allowNegative = await ops.AllowNegativeStockAsync(ct);

        await db.ExecuteInTransactionAsync(async tx =>
        {
            var order = await SalesRules.LoadAsync(db, id, tx);
            var toReserve = order.Confirm(currentUser.UserId, clock.GetUtcNow()); // status rules
            var skus = await SalesRules.SkusAsync(db, toReserve.Select(r => r.VariantId), tx);
            var stocks = await ops.LockAsync(toReserve.Select(r => (order.WarehouseId, r.VariantId)), tx);

            foreach (var (variantId, quantity) in toReserve)
            {
                stocks[(order.WarehouseId, variantId)].Reserve(quantity, allowNegative, skus[variantId]);
            }

            await db.SaveChangesAsync(tx); // a concurrent confirm of the SAME order → version conflict → 409
            return true;
        }, ct);
    }
}

/// <summary>Cancelling hands any reserved stock back to "available", in the same transaction.</summary>
public sealed class CancelSalesOrderHandler(IAppDbContext db, IValidator<CancelSalesOrderCommand> validator, StockOperations ops, TimeProvider clock)
{
    public async Task Handle(Guid id, CancelSalesOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        await ops.EnsureWarehouseUsableAsync(await SalesRules.WarehouseOfAsync(db, id, ct), ct);

        await db.ExecuteInTransactionAsync(async tx =>
        {
            var order = await SalesRules.LoadAsync(db, id, tx);
            var toRelease = order.Cancel(c.Reason, clock.GetUtcNow());

            if (toRelease.Count > 0)
            {
                var stocks = await ops.LockAsync(toRelease.Select(r => (order.WarehouseId, r.VariantId)), tx);
                foreach (var (variantId, quantity) in toRelease)
                {
                    stocks[(order.WarehouseId, variantId)].Release(quantity);
                }
            }

            await db.SaveChangesAsync(tx);
            return true;
        }, ct);
    }
}

/// <summary>
/// Shipping: the order bookkeeping, the DR, the stock decrease (out of the reservation) and the ledger happen in ONE
/// transaction. The ledger line carries the warehouse's average cost at that moment: the cost of goods sold.
/// </summary>
public sealed class ShipSalesOrderHandler(
    IAppDbContext db, IValidator<ShipSalesOrderCommand> validator, StockOperations ops,
    IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<ShipmentResult> Handle(Guid id, ShipSalesOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        await ops.EnsureWarehouseUsableAsync(await SalesRules.WarehouseOfAsync(db, id, ct), ct); // Inventory Staff: only their warehouses

        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var order = await SalesRules.LoadAsync(db, id, tx);
            var now = clock.GetUtcNow();
            var quantities = c.Lines.Where(l => l.Quantity > 0).ToDictionary(l => l.ItemId, l => l.Quantity);

            order.Ship(quantities, now); // status + over-shipment rules

            var number = await numbers.NextAsync(ops.TenantId, "DR", tx);
            var shipment = new SalesShipment(ops.TenantId, number, order.Id, order.WarehouseId, c.Note, currentUser.UserId, now);

            var items = order.Items.Where(i => quantities.ContainsKey(i.Id)).ToList();
            var stocks = await ops.LockAsync(items.Select(i => (order.WarehouseId, i.ProductVariantId)), tx);

            foreach (var item in items)
            {
                var quantity = quantities[item.Id];
                var stock = stocks[(order.WarehouseId, item.ProductVariantId)];
                stock.ShipReserved(quantity);
                shipment.AddItem(item.Id, item.ProductVariantId, quantity, item.UnitPrice, stock.AverageUnitCost);
                ops.Ledger(stock, InventoryMovementType.SalesFulfilment, -quantity, stock.AverageUnitCost, shipment.Id,
                    $"{order.Number} / {number}", c.Note, sourceType: "SalesOrder", sourceId: order.Id);
            }

            db.SalesShipments.Add(shipment);
            // If someone else shipped this order since we loaded it, its version changed → 409, nothing saved.
            await db.SaveChangesAsync(tx);
            return new ShipmentResult(shipment.Id, number, order.Status);
        }, ct);
    }
}
