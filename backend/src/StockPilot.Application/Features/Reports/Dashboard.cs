using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Security;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Purchasing;
using StockPilot.Domain.Sales;

namespace StockPilot.Application.Features.Reports;

public sealed record DashboardQuery
{
    /// <summary>Length of the period, ending today (company time zone). 7, 30 or 90.</summary>
    public int Days { get; init; } = 30;
    public Guid? WarehouseId { get; init; }
}

/// <summary>Each section is null when the user's role can't see it (e.g. Sales Staff get no stock value).</summary>
public sealed record DashboardDto(
    int Days, DateOnly From, DateOnly To, bool CanViewCost,
    StockSummaryDto? Stock, SalesSummaryDto? Sales, PurchasingSummaryDto? Purchasing,
    IReadOnlyList<SalesTrendPointDto>? SalesTrend, IReadOnlyList<TopProductDto>? TopProducts,
    IReadOnlyList<WarehouseStockDto>? StockByWarehouse);

public sealed record StockSummaryDto(decimal? Value, decimal Units, decimal ReservedUnits, int ItemsInStock, int LowStock, int OutOfStock);

/// <summary>Revenue is recognised when goods SHIP (not when an order is taken), so it always has a matching cost.</summary>
public sealed record SalesSummaryDto(
    decimal Revenue, decimal PreviousRevenue, int OrdersShipped, int OpenOrders,
    decimal? GrossProfit, decimal? PreviousGrossProfit, decimal? MarginPercent);

public sealed record PurchasingSummaryDto(int OpenOrders, int AwaitingApproval, int Overdue, decimal IncomingUnits, decimal? IncomingValue);

public sealed record SalesTrendPointDto(DateOnly Date, decimal Revenue, decimal? GrossProfit);

public sealed record TopProductDto(Guid ProductId, string ProductName, decimal Quantity, decimal Revenue);

public sealed record WarehouseStockDto(Guid WarehouseId, string Code, string Name, decimal Units, decimal? Value, int LowStock, int OutOfStock);

public sealed class GetDashboardHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions, ReportClock reportClock)
{
    public async Task<DashboardDto> Handle(DashboardQuery query, CancellationToken ct)
    {
        var days = query.Days is 7 or 30 or 90 ? query.Days : 30;
        var zone = await reportClock.TimeZoneAsync(ct);
        var now = reportClock.Now;
        var today = ReportClock.Today(zone, now);
        var from = today.AddDays(-(days - 1));
        var start = ReportClock.StartOf(from, zone);
        var previousStart = ReportClock.StartOf(from.AddDays(-days), zone);

        var warehouseIds = await ReportScope.WarehousesAsync(access, query.WarehouseId, ct);

        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);
        var canSeeStock = await permissions.HasAsync(Permissions.Inventory.View, ct);
        var canSeeSales = await permissions.HasAsync(Permissions.Sales.View, ct);
        var canSeePurchases = await permissions.HasAsync(Permissions.Purchases.View, ct);

        StockSummaryDto? stock = null;
        IReadOnlyList<WarehouseStockDto>? byWarehouse = null;
        if (canSeeStock)
        {
            byWarehouse = await StockByWarehouseAsync(warehouseIds, canViewCost, ct);
            stock = new StockSummaryDto(
                canViewCost ? byWarehouse.Sum(w => w.Value ?? 0) : null,
                byWarehouse.Sum(w => w.Units),
                await ReservedUnitsAsync(warehouseIds, ct),
                await ItemsInStockAsync(warehouseIds, ct),
                byWarehouse.Sum(w => w.LowStock),
                byWarehouse.Sum(w => w.OutOfStock));
        }

        SalesSummaryDto? sales = null;
        IReadOnlyList<SalesTrendPointDto>? trend = null;
        IReadOnlyList<TopProductDto>? top = null;
        if (canSeeSales)
        {
            (sales, trend, top) = await SalesAsync(warehouseIds, zone, from, today, start, previousStart, canViewCost, ct);
        }

        var purchasing = canSeePurchases ? await PurchasingAsync(warehouseIds, today, canViewCost, ct) : null;

        return new DashboardDto(days, from, today, canViewCost, stock, sales, purchasing, trend, top, byWarehouse);
    }

    private async Task<IReadOnlyList<WarehouseStockDto>> StockByWarehouseAsync(IReadOnlyCollection<Guid>? warehouseIds, bool canViewCost, CancellationToken ct)
    {
        var rows =
            from s in db.WarehouseStocks.AsNoTracking()
            join w in db.Warehouses on s.WarehouseId equals w.Id
            join v in db.ProductVariants on s.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where w.IsActive && v.IsActive && p.Status == ProductStatus.Active
            select new { s, w, v };
        if (warehouseIds is not null) rows = rows.Where(r => warehouseIds.Contains(r.w.Id));

        var grouped = await rows
            .GroupBy(r => new { r.w.Id, r.w.Code, r.w.Name })
            .Select(g => new
            {
                g.Key.Id, g.Key.Code, g.Key.Name,
                Units = g.Sum(r => r.s.QuantityOnHand),
                Value = g.Sum(r => r.s.QuantityOnHand * r.s.AverageUnitCost),
                Low = g.Count(r => r.s.QuantityOnHand - r.s.QuantityReserved > 0 && r.s.QuantityOnHand - r.s.QuantityReserved <= r.v.ReorderLevel),
                Out = g.Count(r => r.s.QuantityOnHand - r.s.QuantityReserved <= 0),
            })
            .OrderBy(g => g.Code)
            .ToListAsync(ct);

        return grouped.Select(g => new WarehouseStockDto(g.Id, g.Code, g.Name, g.Units, canViewCost ? decimal.Round(g.Value, 2) : null, g.Low, g.Out)).ToList();
    }

    private Task<decimal> ReservedUnitsAsync(IReadOnlyCollection<Guid>? warehouseIds, CancellationToken ct) =>
        db.WarehouseStocks.Where(s => warehouseIds == null || warehouseIds.Contains(s.WarehouseId)).SumAsync(s => s.QuantityReserved, ct);

    /// <summary>Distinct variants with something on hand (an item in two warehouses counts once).</summary>
    private Task<int> ItemsInStockAsync(IReadOnlyCollection<Guid>? warehouseIds, CancellationToken ct) =>
        db.WarehouseStocks.Where(s => s.QuantityOnHand > 0 && (warehouseIds == null || warehouseIds.Contains(s.WarehouseId)))
            .Select(s => s.ProductVariantId).Distinct().CountAsync(ct);

    private async Task<(SalesSummaryDto, IReadOnlyList<SalesTrendPointDto>, IReadOnlyList<TopProductDto>)> SalesAsync(
        IReadOnlyCollection<Guid>? warehouseIds, TimeZoneInfo zone, DateOnly from, DateOnly today,
        DateTimeOffset start, DateTimeOffset previousStart, bool canViewCost, CancellationToken ct)
    {
        // Shipped lines of the current AND previous period, bucketed by local day here, where the company's time zone
        // is known exactly. Numbers only: product NAMES are fetched afterwards for the top 5 alone (performance pass:
        // carrying a name on each of ~30,000 lines for 90 days cost more than the query itself).
        var lines = await (
            from si in db.SalesShipmentItems.AsNoTracking()
            join sh in db.SalesShipments on si.SalesShipmentId equals sh.Id
            join v in db.ProductVariants on si.ProductVariantId equals v.Id
            where sh.ShippedAt >= previousStart && (warehouseIds == null || warehouseIds.Contains(sh.WarehouseId))
            select new { sh.ShippedAt, sh.SalesOrderId, v.ProductId, si.Quantity, si.UnitPrice, si.UnitCost })
            .ToListAsync(ct);

        var current = lines.Where(l => l.ShippedAt >= start).ToList();
        var previous = lines.Where(l => l.ShippedAt < start).ToList();

        var revenue = decimal.Round(current.Sum(l => l.Quantity * l.UnitPrice), 2);
        var cost = decimal.Round(current.Sum(l => l.Quantity * l.UnitCost), 2);
        var previousRevenue = decimal.Round(previous.Sum(l => l.Quantity * l.UnitPrice), 2);
        var previousCost = decimal.Round(previous.Sum(l => l.Quantity * l.UnitCost), 2);

        var openOrders = await db.SalesOrders.CountAsync(o =>
            (o.Status == SalesOrderStatus.Confirmed || o.Status == SalesOrderStatus.PartiallyFulfilled)
            && (warehouseIds == null || warehouseIds.Contains(o.WarehouseId)), ct);

        var summary = new SalesSummaryDto(
            revenue, previousRevenue, current.Select(l => l.SalesOrderId).Distinct().Count(), openOrders,
            canViewCost ? revenue - cost : null,
            canViewCost ? previousRevenue - previousCost : null,
            canViewCost && revenue > 0 ? decimal.Round((revenue - cost) / revenue * 100, 1) : null);

        // One point per day, including days with no sales (a gap in a line chart would read as "no data").
        var byDay = current.GroupBy(l => ReportClock.LocalDate(l.ShippedAt, zone)).ToDictionary(g => g.Key, g => g.ToList());
        var trend = new List<SalesTrendPointDto>();
        for (var day = from; day <= today; day = day.AddDays(1))
        {
            var dayLines = byDay.GetValueOrDefault(day) ?? [];
            var dayRevenue = decimal.Round(dayLines.Sum(l => l.Quantity * l.UnitPrice), 2);
            var dayCost = decimal.Round(dayLines.Sum(l => l.Quantity * l.UnitCost), 2);
            trend.Add(new SalesTrendPointDto(day, dayRevenue, canViewCost ? dayRevenue - dayCost : null));
        }

        var byProduct = current
            .GroupBy(l => l.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(l => l.Quantity), Revenue: decimal.Round(g.Sum(l => l.Quantity * l.UnitPrice), 2)))
            .OrderByDescending(t => t.Revenue)
            .ToList();
        // Names for the top 5 plus anything tied with the 5th (ties are broken by name, so those need names too).
        var cutoff = byProduct.Count > 5 ? byProduct[4].Revenue : decimal.MinValue;
        var contenders = byProduct.Where(t => t.Revenue >= cutoff).ToList();
        var ids = contenders.Select(t => t.ProductId).ToList();
        var names = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var top = contenders
            .Select(t => new TopProductDto(t.ProductId, names[t.ProductId], t.Quantity, t.Revenue))
            .OrderByDescending(t => t.Revenue).ThenBy(t => t.ProductName)
            .Take(5)
            .ToList();

        return (summary, trend, top);
    }

    private async Task<PurchasingSummaryDto> PurchasingAsync(IReadOnlyCollection<Guid>? warehouseIds, DateOnly today, bool canViewCost, CancellationToken ct)
    {
        var orders = db.PurchaseOrders.AsNoTracking().Where(p => warehouseIds == null || warehouseIds.Contains(p.WarehouseId));

        var open = await orders.CountAsync(p => p.Status == PurchaseOrderStatus.Submitted
            || p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived, ct);
        var awaiting = await orders.CountAsync(p => p.Status == PurchaseOrderStatus.Submitted, ct);
        var overdue = await orders.CountAsync(p => (p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived)
            && p.ExpectedDate != null && p.ExpectedDate < today, ct);

        // What approved orders will still bring in.
        var incoming = await (
            from i in db.PurchaseOrderItems.AsNoTracking()
            join p in orders on i.PurchaseOrderId equals p.Id
            where p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived
            select new { Remaining = i.QuantityOrdered - i.QuantityReceived, i.UnitCost })
            .ToListAsync(ct);

        return new PurchasingSummaryDto(open, awaiting, overdue, incoming.Sum(i => i.Remaining),
            canViewCost ? decimal.Round(incoming.Sum(i => i.Remaining * i.UnitCost), 2) : null);
    }
}
