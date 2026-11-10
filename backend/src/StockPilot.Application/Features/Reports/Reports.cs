using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Security;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Purchasing;
using StockPilot.Domain.Sales;

namespace StockPilot.Application.Features.Reports;

public enum ReportColumnKind
{
    Text = 1,
    Quantity = 2,
    Money = 3,
    Percent = 4,
    Date = 5,
    DateTime = 6,
}

public sealed record ReportColumnDto(string Key, string Label, ReportColumnKind Kind);

/// <summary>
/// Every report has the same shape: columns + rows (+ totals). One screen renders them all and one writer turns
/// them into CSV, so a new report is just a new query. Cost columns are simply left out for roles without
/// inventory.view_cost.
/// </summary>
public sealed record ReportTableDto(
    string Key, string Title, string Description, DateOnly? From, DateOnly? To,
    IReadOnlyList<ReportColumnDto> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<object?>? Totals, bool Truncated);

public sealed record ReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? WarehouseId { get; init; }

    /// <summary>sales: product | customer | day. purchases: supplier | product.</summary>
    public string? GroupBy { get; init; }

    /// <summary>stock-movements only.</summary>
    public InventoryMovementType? Type { get; init; }
}

public static class ReportKeys
{
    public const string InventoryValuation = "inventory-valuation";
    public const string LowStock = "low-stock";
    public const string Sales = "sales";
    public const string Purchases = "purchases";
    public const string StockMovements = "stock-movements";

    public static readonly IReadOnlyList<string> All = [InventoryValuation, LowStock, Sales, Purchases, StockMovements];
}

public sealed class RunReportHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions, ReportClock reportClock)
{
    /// <summary>Hard cap per report so one request can't pull the whole ledger into memory.</summary>
    public const int MaxRows = 5000;

    public async Task<ReportTableDto> Handle(string key, ReportQuery query, CancellationToken ct)
    {
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);
        var warehouses = await ReportScope.WarehousesAsync(access, query.WarehouseId, ct);

        return key switch
        {
            ReportKeys.InventoryValuation => await InventoryValuationAsync(warehouses, canViewCost, ct),
            ReportKeys.LowStock => await LowStockAsync(warehouses, ct),
            ReportKeys.Sales => await SalesAsync(query, warehouses, canViewCost, ct),
            ReportKeys.Purchases => await PurchasesAsync(query, warehouses, canViewCost, ct),
            ReportKeys.StockMovements => await MovementsAsync(query, warehouses, canViewCost, ct),
            _ => throw new NotFoundException("Report", key),
        };
    }

    // ---------------- helpers ----------------

    private static ReportColumnDto Col(string key, string label, ReportColumnKind kind = ReportColumnKind.Text) => new(key, label, kind);

    /// <summary>Drops cost columns (and the matching cells) for roles that may not see costs.</summary>
    private static (List<ReportColumnDto> Columns, Func<object?[], object?[]> Project) WithoutCost(
        IReadOnlyList<(ReportColumnDto Column, bool IsCost)> columns, bool canViewCost)
    {
        var keep = columns.Select((c, i) => (c, i)).Where(x => canViewCost || !x.c.IsCost).Select(x => x.i).ToArray();
        return (keep.Select(i => columns[i].Column).ToList(), row => keep.Select(i => row[i]).ToArray());
    }

    private async Task<(DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset End)> PeriodAsync(ReportQuery query, CancellationToken ct)
    {
        var zone = await reportClock.TimeZoneAsync(ct);
        var to = query.To ?? ReportClock.Today(zone, reportClock.Now);
        var from = query.From ?? to.AddDays(-29);
        if (from > to)
        {
            throw new DomainException("PERIOD_INVALID", "The start date must be on or before the end date.");
        }

        if (to.DayNumber - from.DayNumber > 366)
        {
            throw new DomainException("PERIOD_TOO_LONG", "Choose a period of at most one year.");
        }

        return (from, to, ReportClock.StartOf(from, zone), ReportClock.StartOf(to.AddDays(1), zone));
    }

    private static ReportTableDto Table(
        string key, string title, string description, DateOnly? from, DateOnly? to,
        List<ReportColumnDto> columns, List<object?[]> rows, object?[]? totals) =>
        new(key, title, description, from, to, columns,
            rows.Take(MaxRows).Select(r => (IReadOnlyList<object?>)r).ToList(), totals, rows.Count > MaxRows);

    private static decimal Money(decimal value) => decimal.Round(value, 2);

    private static decimal? Margin(decimal revenue, decimal cost) => revenue == 0 ? null : decimal.Round((revenue - cost) / revenue * 100, 1);

    // ---------------- 1. Inventory valuation ----------------

    private async Task<ReportTableDto> InventoryValuationAsync(IReadOnlyCollection<Guid>? warehouses, bool canViewCost, CancellationToken ct)
    {
        var data = await (
            from s in db.WarehouseStocks.AsNoTracking()
            join w in db.Warehouses on s.WarehouseId equals w.Id
            join v in db.ProductVariants on s.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where (s.QuantityOnHand != 0 || s.QuantityReserved != 0) && (warehouses == null || warehouses.Contains(w.Id))
            orderby w.Code, p.Name, v.Sku
            select new { w.Code, v.Sku, p.Name, Variant = v.Name, p.UnitOfMeasure, s.QuantityOnHand, s.QuantityReserved, s.AverageUnitCost })
            .Take(MaxRows + 1)
            .ToListAsync(ct);

        var (columns, project) = WithoutCost(
        [
            (Col("warehouse", "Warehouse"), false), (Col("sku", "SKU"), false), (Col("product", "Product"), false),
            (Col("unit", "Unit"), false), (Col("onHand", "On hand", ReportColumnKind.Quantity), false),
            (Col("reserved", "Reserved", ReportColumnKind.Quantity), false), (Col("available", "Available", ReportColumnKind.Quantity), false),
            (Col("avgCost", "Average cost", ReportColumnKind.Money), true), (Col("value", "Value", ReportColumnKind.Money), true),
        ], canViewCost);

        var rows = data.Select(d => project(
        [
            d.Code, d.Sku, d.Variant is null ? d.Name : $"{d.Name} ({d.Variant})", d.UnitOfMeasure,
            d.QuantityOnHand, d.QuantityReserved, d.QuantityOnHand - d.QuantityReserved,
            decimal.Round(d.AverageUnitCost, 2), Money(d.QuantityOnHand * d.AverageUnitCost),
        ])).ToList();

        var totals = project(["Total", null, null, null, data.Sum(d => d.QuantityOnHand), data.Sum(d => d.QuantityReserved),
            data.Sum(d => d.QuantityOnHand - d.QuantityReserved), null, Money(data.Sum(d => d.QuantityOnHand * d.AverageUnitCost))]);

        return Table(ReportKeys.InventoryValuation, "Inventory valuation",
            "What is on hand in each warehouse right now, valued at weighted average cost.", null, null, columns, rows, totals);
    }

    // ---------------- 2. Low stock ----------------

    private async Task<ReportTableDto> LowStockAsync(IReadOnlyCollection<Guid>? warehouses, CancellationToken ct)
    {
        var data = await (
            from s in db.WarehouseStocks.AsNoTracking()
            join w in db.Warehouses on s.WarehouseId equals w.Id
            join v in db.ProductVariants on s.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where w.IsActive && v.IsActive && p.Status == ProductStatus.Active
                && s.QuantityOnHand - s.QuantityReserved <= v.ReorderLevel
                && (warehouses == null || warehouses.Contains(w.Id))
            orderby s.QuantityOnHand - s.QuantityReserved, p.Name
            select new { WarehouseId = w.Id, w.Code, VariantId = v.Id, v.Sku, p.Name, Variant = v.Name, Available = s.QuantityOnHand - s.QuantityReserved, v.ReorderLevel })
            .Take(MaxRows + 1)
            .ToListAsync(ct);

        // Already on its way: what approved purchase orders to that warehouse still have to deliver.
        var onOrder = (await (
                from i in db.PurchaseOrderItems.AsNoTracking()
                join po in db.PurchaseOrders on i.PurchaseOrderId equals po.Id
                where po.Status == PurchaseOrderStatus.Approved || po.Status == PurchaseOrderStatus.PartiallyReceived
                group i by new { po.WarehouseId, i.ProductVariantId } into g
                select new { g.Key.WarehouseId, g.Key.ProductVariantId, Remaining = g.Sum(i => i.QuantityOrdered - i.QuantityReceived) })
            .ToListAsync(ct))
            .ToDictionary(x => (x.WarehouseId, x.ProductVariantId), x => x.Remaining);

        List<ReportColumnDto> columns =
        [
            Col("warehouse", "Warehouse"), Col("sku", "SKU"), Col("product", "Product"), Col("status", "Status"),
            Col("available", "Available", ReportColumnKind.Quantity), Col("reorderLevel", "Reorder level", ReportColumnKind.Quantity),
            Col("shortfall", "Below reorder by", ReportColumnKind.Quantity), Col("onOrder", "On order", ReportColumnKind.Quantity),
        ];

        var rows = data.Select(d => new object?[]
        {
            d.Code, d.Sku, d.Variant is null ? d.Name : $"{d.Name} ({d.Variant})", d.Available <= 0 ? "Out of stock" : "Low",
            d.Available, d.ReorderLevel, d.ReorderLevel - d.Available, onOrder.GetValueOrDefault((d.WarehouseId, d.VariantId)),
        }).ToList();

        return Table(ReportKeys.LowStock, "Low stock",
            "Items whose available quantity is at or below their reorder level, with what is already on order.", null, null, columns, rows, null);
    }

    // ---------------- 3. Sales ----------------

    private async Task<ReportTableDto> SalesAsync(ReportQuery query, IReadOnlyCollection<Guid>? warehouses, bool canViewCost, CancellationToken ct)
    {
        var (from, to, start, end) = await PeriodAsync(query, ct);
        var zone = await reportClock.TimeZoneAsync(ct);
        var groupBy = query.GroupBy is "customer" or "day" ? query.GroupBy : "product";

        var lines = await (
            from si in db.SalesShipmentItems.AsNoTracking()
            join sh in db.SalesShipments on si.SalesShipmentId equals sh.Id
            join so in db.SalesOrders on sh.SalesOrderId equals so.Id
            join c in db.Customers on so.CustomerId equals c.Id
            join v in db.ProductVariants on si.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where sh.ShippedAt >= start && sh.ShippedAt < end && (warehouses == null || warehouses.Contains(sh.WarehouseId))
            select new { sh.ShippedAt, so.Id, Customer = c.Name, p.Name, Variant = v.Name, v.Sku, si.Quantity, si.UnitPrice, si.UnitCost })
            .ToListAsync(ct);

        var groups = groupBy switch
        {
            "customer" => lines.GroupBy(l => (Key: l.Customer, Sort: l.Customer)),
            "day" => lines.GroupBy(l => { var d = ReportClock.LocalDate(l.ShippedAt, zone); return (Key: d.ToString("yyyy-MM-dd"), Sort: d.ToString("yyyy-MM-dd")); }),
            _ => lines.GroupBy(l => (Key: l.Variant is null ? $"{l.Name} · {l.Sku}" : $"{l.Name} ({l.Variant}) · {l.Sku}", Sort: l.Name)),
        };

        var labelColumn = groupBy == "day" ? Col("date", "Date", ReportColumnKind.Date) : Col("name", groupBy == "customer" ? "Customer" : "Product");
        var (columns, project) = WithoutCost(
        [
            (labelColumn, false),
            (Col("orders", "Orders", ReportColumnKind.Quantity), false), (Col("quantity", "Quantity", ReportColumnKind.Quantity), false),
            (Col("revenue", "Revenue", ReportColumnKind.Money), false), (Col("cost", "Cost of goods", ReportColumnKind.Money), true),
            (Col("profit", "Gross profit", ReportColumnKind.Money), true), (Col("margin", "Margin", ReportColumnKind.Percent), true),
        ], canViewCost);

        var rows = groups
            .Select(g =>
            {
                var revenue = Money(g.Sum(l => l.Quantity * l.UnitPrice));
                var cost = Money(g.Sum(l => l.Quantity * l.UnitCost));
                object? label = groupBy == "day" ? DateOnly.ParseExact(g.Key.Key, "yyyy-MM-dd") : g.Key.Key;
                return (Sort: g.Key.Sort, Revenue: revenue, Cells: project([label, g.Select(l => l.Id).Distinct().Count(), g.Sum(l => l.Quantity), revenue, cost, revenue - cost, Margin(revenue, cost)]));
            })
            .OrderBy(r => groupBy == "day" ? r.Sort : null, StringComparer.Ordinal)
            .ThenByDescending(r => groupBy == "day" ? 0 : r.Revenue)
            .Select(r => r.Cells)
            .ToList();

        var totalRevenue = Money(lines.Sum(l => l.Quantity * l.UnitPrice));
        var totalCost = Money(lines.Sum(l => l.Quantity * l.UnitCost));
        var totals = project(["Total", lines.Select(l => l.Id).Distinct().Count(), lines.Sum(l => l.Quantity), totalRevenue, totalCost, totalRevenue - totalCost, Margin(totalRevenue, totalCost)]);

        return Table(ReportKeys.Sales, "Sales",
            "Goods shipped to customers in the period. Revenue counts when goods ship.", from, to, columns, rows, totals);
    }

    // ---------------- 4. Purchases ----------------

    private async Task<ReportTableDto> PurchasesAsync(ReportQuery query, IReadOnlyCollection<Guid>? warehouses, bool canViewCost, CancellationToken ct)
    {
        var (from, to, start, end) = await PeriodAsync(query, ct);
        var bySupplier = query.GroupBy != "product";

        var lines = await (
            from ri in db.PurchaseReceiptItems.AsNoTracking()
            join r in db.PurchaseReceipts on ri.PurchaseReceiptId equals r.Id
            join po in db.PurchaseOrders on r.PurchaseOrderId equals po.Id
            join s in db.Suppliers on po.SupplierId equals s.Id
            join v in db.ProductVariants on ri.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            where r.ReceivedAt >= start && r.ReceivedAt < end && (warehouses == null || warehouses.Contains(r.WarehouseId))
            select new { ReceiptId = r.Id, Supplier = s.Name, p.Name, Variant = v.Name, v.Sku, ri.Quantity, ri.UnitCost })
            .ToListAsync(ct);

        var (columns, project) = WithoutCost(
        [
            (Col("name", bySupplier ? "Supplier" : "Product"), false), (Col("receipts", "Deliveries", ReportColumnKind.Quantity), false),
            (Col("quantity", "Quantity", ReportColumnKind.Quantity), false), (Col("value", "Value", ReportColumnKind.Money), true),
        ], canViewCost);

        var rows = lines
            .GroupBy(l => bySupplier ? l.Supplier : l.Variant is null ? $"{l.Name} · {l.Sku}" : $"{l.Name} ({l.Variant}) · {l.Sku}")
            .Select(g => (Value: Money(g.Sum(l => l.Quantity * l.UnitCost)), Cells: project([g.Key, g.Select(l => l.ReceiptId).Distinct().Count(), g.Sum(l => l.Quantity), Money(g.Sum(l => l.Quantity * l.UnitCost))])))
            .OrderByDescending(r => r.Value)
            .Select(r => r.Cells)
            .ToList();

        var totals = project(["Total", lines.Select(l => l.ReceiptId).Distinct().Count(), lines.Sum(l => l.Quantity), Money(lines.Sum(l => l.Quantity * l.UnitCost))]);

        return Table(ReportKeys.Purchases, "Purchases",
            "Goods received from suppliers in the period, valued at purchase order cost.", from, to, columns, rows, totals);
    }

    // ---------------- 5. Stock movements ----------------

    private async Task<ReportTableDto> MovementsAsync(ReportQuery query, IReadOnlyCollection<Guid>? warehouses, bool canViewCost, CancellationToken ct)
    {
        var (from, to, start, end) = await PeriodAsync(query, ct);

        var rowsQuery =
            from t in db.InventoryTransactions.AsNoTracking()
            join w in db.Warehouses on t.WarehouseId equals w.Id
            join v in db.ProductVariants on t.ProductVariantId equals v.Id
            join p in db.Products on v.ProductId equals p.Id
            join u in db.Users on t.PerformedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            where t.OccurredAt >= start && t.OccurredAt < end && (warehouses == null || warehouses.Contains(t.WarehouseId))
            select new { t, w.Code, v.Sku, p.Name, By = u != null ? u.FirstName + " " + u.LastName : null };
        if (query.Type is { } type) rowsQuery = rowsQuery.Where(r => r.t.Type == type);

        var data = await rowsQuery.OrderByDescending(r => r.t.OccurredAt).ThenByDescending(r => r.t.Id).Take(MaxRows + 1).ToListAsync(ct);

        var (columns, project) = WithoutCost(
        [
            (Col("occurredAt", "Date", ReportColumnKind.DateTime), false), (Col("type", "Movement"), false), (Col("warehouse", "Warehouse"), false),
            (Col("sku", "SKU"), false), (Col("product", "Product"), false), (Col("change", "Change", ReportColumnKind.Quantity), false),
            (Col("balance", "Balance after", ReportColumnKind.Quantity), false), (Col("unitCost", "Unit cost", ReportColumnKind.Money), true),
            (Col("reference", "Reference"), false), (Col("by", "By"), false),
        ], canViewCost);

        var rows = data.Select(r => project(
        [
            r.t.OccurredAt, MovementLabel(r.t.Type), r.Code, r.Sku, r.Name, r.t.QuantityChange, r.t.QuantityAfter,
            decimal.Round(r.t.UnitCost, 2), r.t.Reference, r.By,
        ])).ToList();

        return Table(ReportKeys.StockMovements, "Stock movements",
            "Every change to stock in the period, newest first, straight from the ledger.", from, to, columns, rows, null);
    }

    public static string MovementLabel(InventoryMovementType type) => type switch
    {
        InventoryMovementType.StockIn => "Stock in",
        InventoryMovementType.StockOut => "Stock out",
        InventoryMovementType.AdjustmentIncrease => "Adjustment (+)",
        InventoryMovementType.AdjustmentDecrease => "Adjustment (−)",
        InventoryMovementType.TransferOut => "Transfer out",
        InventoryMovementType.TransferIn => "Transfer in",
        InventoryMovementType.PurchaseReceipt => "Purchase receipt",
        InventoryMovementType.SalesFulfilment => "Sale shipped",
        _ => type.ToString(),
    };
}
