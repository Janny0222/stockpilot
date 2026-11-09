using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Sales;

namespace StockPilot.Infrastructure.Persistence.Seed;

/// <summary>
/// Demo customers and sales orders in every status, plus a month of fulfilled orders so the dashboard has real
/// history to chart. Everything goes through the same domain methods as the API (Reserve → ShipReserved, ledger
/// lines at average cost), so stock, reservations and the ledger still reconcile.
/// </summary>
internal static class DemoSales
{
    private static readonly (string Code, string Name, string Contact, string Email, string Phone, int Terms)[] Customers =
    [
        ("BUILDRIGHT", "BuildRight Construction Co.", "Engr. Mark Villanueva", "purchasing@buildright.example", "+63 917 555 0142", 30),
        ("CASA", "Casa Home Depot – Quezon City", "Joy Aquino", "orders@casahome.example", "+63 2 8901 2233", 15),
        ("SUNRISE", "Sunrise Electrical Services", "Dennis Ramos", "dennis@sunrise-elec.example", "+63 918 222 7788", 0),
        ("METROFIX", "MetroFix Facilities Management", "Carla Uy", "procurement@metrofix.example", "+63 2 8777 6100", 45),
        ("WALKIN", "Walk-in customer", "Counter sales", "", "", 0),
    ];

    public static async Task SeedAsync(AppDbContext db, Guid tenantId, Guid? ownerId, ILogger logger, CancellationToken ct)
    {
        if (await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId, ct))
        {
            return;
        }

        var manila = await db.Warehouses.IgnoreQueryFilters().Where(w => w.TenantId == tenantId && w.Code == "MNL").Select(w => (Guid?)w.Id).SingleOrDefaultAsync(ct);
        if (manila is null)
        {
            return;
        }

        var salesUser = await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.RoleId == SystemRoles.SalesStaffId).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct) ?? ownerId;

        var customers = Customers.ToDictionary(c => c.Code, c =>
        {
            var customer = new Customer(tenantId, c.Code, c.Name);
            customer.Update(c.Code, c.Name, c.Contact, c.Email, c.Phone, null, c.Terms, null);
            db.Customers.Add(customer);
            return customer;
        });

        var variants = await db.ProductVariants.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.IsActive)
            .ToDictionaryAsync(v => v.Sku, v => new { v.Id, v.SellingPrice }, ct);

        var stocks = await db.WarehouseStocks.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.WarehouseId == manila.Value)
            .ToDictionaryAsync(s => s.ProductVariantId, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var number = 0;
        var shipmentNumber = 0;

        // Only lines the warehouse can actually cover (keeps the seed valid whatever the stock levels are).
        SalesOrder? Order(string customer, int daysAgo, params (string Sku, decimal Qty)[] lines)
        {
            var usable = lines
                .Where(l => variants.ContainsKey(l.Sku) && stocks.TryGetValue(variants[l.Sku].Id, out var s) && s.QuantityAvailable >= l.Qty)
                .ToList();
            if (usable.Count == 0)
            {
                return null;
            }

            var order = new SalesOrder(tenantId, $"SO-{++number:D6}", customers[customer].Id, manila.Value, today.AddDays(-daysAgo), null);
            order.ReplaceItems(usable.Select(l => (variants[l.Sku].Id, l.Qty, variants[l.Sku].SellingPrice)));
            db.SalesOrders.Add(order);
            return order;
        }

        void Confirm(SalesOrder? order, int daysAgo)
        {
            if (order is null) return;
            foreach (var (variantId, quantity) in order.Confirm(salesUser, now.AddDays(-daysAgo)))
            {
                stocks[variantId].Reserve(quantity, allowNegative: false);
            }
        }

        void Ship(SalesOrder? order, int daysAgo, bool all)
        {
            if (order is null) return;
            var at = now.AddDays(-daysAgo);
            var quantities = order.Items.ToDictionary(i => i.Id, i => all ? i.QuantityRemaining : Math.Floor(i.QuantityRemaining / 2));
            if (quantities.Values.All(q => q == 0)) return;
            order.Ship(quantities, at);

            var shipment = new SalesShipment(tenantId, $"DR-{++shipmentNumber:D6}", order.Id, order.WarehouseId, null, ownerId, at);
            foreach (var item in order.Items.Where(i => quantities[i.Id] > 0))
            {
                var qty = quantities[item.Id];
                var stock = stocks[item.ProductVariantId];
                stock.ShipReserved(qty);
                shipment.AddItem(item.Id, item.ProductVariantId, qty, item.UnitPrice, stock.AverageUnitCost);
                db.InventoryTransactions.Add(new InventoryTransaction(
                    tenantId, order.WarehouseId, item.ProductVariantId, InventoryMovementType.SalesFulfilment, -qty, stock.QuantityOnHand,
                    stock.AverageUnitCost, shipment.Id, $"{order.Number} / {shipment.Number}", null, null, "SalesOrder", order.Id, ownerId, at));
            }

            db.SalesShipments.Add(shipment);
        }

        // A month of fulfilled orders (history for the dashboard).
        var history = new (string Customer, int DaysAgo, (string, decimal)[] Lines)[]
        {
            ("BUILDRIGHT", 28, [("THHN-35-RED", 2), ("CB-1P-20A", 6)]),
            ("CASA", 25, [("LED-E27-9W", 24), ("LED-E27-12W", 12)]),
            ("SUNRISE", 22, [("LED-T8-18-DL", 10), ("CB-1P-20A", 4)]),
            ("WALKIN", 19, [("HAM-16OZ", 2), ("TFL-12", 10)]),
            ("METROFIX", 16, [("LED-E27-9W", 30), ("LED-T8-18-DL", 12)]),
            ("CASA", 12, [("PNT-LTX-WHT-1G", 6), ("BRS-2IN", 12)]),
            ("BUILDRIGHT", 9, [("PVC-BL-12", 20), ("PVC-ELB-12", 40)]),
            ("WALKIN", 6, [("LED-E27-12W", 4), ("TFL-12", 6)]),
            ("SUNRISE", 3, [("THHN-35-BLK", 1), ("CB-1P-20A", 3)]),
        };
        foreach (var (customer, daysAgo, lines) in history)
        {
            var order = Order(customer, daysAgo, lines);
            Confirm(order, daysAgo);
            Ship(order, Math.Max(0, daysAgo - 1), all: true);
        }

        // Partially fulfilled: half shipped, the rest still reserved.
        var partial = Order("METROFIX", 4, ("LED-E27-9W", 40), ("LED-E27-12W", 20));
        Confirm(partial, 4);
        Ship(partial, 2, all: false);

        // Confirmed, waiting to ship (its stock shows as reserved on the Inventory page).
        var confirmed = Order("BUILDRIGHT", 1, ("CB-1P-20A", 10), ("THHN-35-RED", 1));
        Confirm(confirmed, 1);

        // Draft still being quoted.
        Order("CASA", 0, ("PNT-LTX-WHT-1G", 10), ("BRS-2IN", 20));

        // Cancelled before confirming.
        var cancelled = Order("SUNRISE", 7, ("LED-T8-18-DL", 6));
        cancelled?.Cancel("Customer bought elsewhere", now.AddDays(-6));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo sales: {Customers} customers, {Orders} sales orders, {Shipments} shipments",
            customers.Count, number, shipmentNumber);
    }

    /// <summary>Keeps the SO/DR counters ahead of the seeded numbers.</summary>
    public static Task SyncSequencesAsync(AppDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tenant_sequences (tenant_id, name, next_value)
            SELECT {tenantId}, s.name, s.value FROM (VALUES ('SO', (SELECT count(*) FROM sales_orders WHERE tenant_id = {tenantId})),
                                                       ('DR', (SELECT count(*) FROM sales_shipments WHERE tenant_id = {tenantId}))) AS s(name, value)
            WHERE s.value > 0
            ON CONFLICT (tenant_id, name) DO UPDATE SET next_value = GREATEST(tenant_sequences.next_value, EXCLUDED.next_value)
            """, ct);
}
