using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Purchasing;

namespace StockPilot.Infrastructure.Persistence.Seed;

/// <summary>
/// Demo suppliers and purchase orders in every status, so the Purchases screens tell a story out of the box:
/// one received in full, one partially received, one approved and awaiting delivery, one submitted, one draft.
/// Receipts go through the same domain methods and ledger as the real API, so stock still reconciles.
/// </summary>
internal static class DemoPurchasing
{
    private static readonly (string Code, string Name, string Contact, string Email, string Phone, int Terms)[] Suppliers =
    [
        ("PHELEC", "Philippine Electrical Supply Corp.", "Ramon Dizon", "orders@phelec.example", "+63 2 8123 4567", 30),
        ("LUMEN", "Lumen Lighting Distributors", "Grace Tan", "sales@lumen.example", "+63 2 8765 4321", 15),
        ("AQUAFLO", "AquaFlo Plumbing Trading", "Benjie Lopez", "po@aquaflo.example", "+63 32 234 5678", 30),
        ("TOOLMASTER", "ToolMaster Hardware Inc.", "Liza Mercado", "trade@toolmaster.example", "+63 2 8555 0101", 45),
        ("COLORWORKS", "ColorWorks Paints & Coatings", "Arnel Cruz", "dealer@colorworks.example", "+63 82 300 1200", 0),
    ];

    public static async Task SeedAsync(AppDbContext db, Guid tenantId, Guid? ownerId, ILogger logger, CancellationToken ct)
    {
        if (await db.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId, ct))
        {
            return;
        }

        var manila = await db.Warehouses.IgnoreQueryFilters().Where(w => w.TenantId == tenantId && w.Code == "MNL").Select(w => (Guid?)w.Id).SingleOrDefaultAsync(ct);
        var cebu = await db.Warehouses.IgnoreQueryFilters().Where(w => w.TenantId == tenantId && w.Code == "CEB").Select(w => (Guid?)w.Id).SingleOrDefaultAsync(ct);
        if (manila is null)
        {
            return;
        }

        var suppliers = Suppliers.ToDictionary(s => s.Code, s =>
        {
            var supplier = new Supplier(tenantId, s.Code, s.Name);
            supplier.Update(s.Code, s.Name, s.Contact, s.Email, s.Phone, null, s.Terms, null);
            db.Suppliers.Add(supplier);
            return supplier;
        });

        var variants = await db.ProductVariants.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.IsActive)
            .ToDictionaryAsync(v => v.Sku, v => new { v.Id, v.CostPrice }, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        var number = 0;
        var receiptNumber = 0;

        PurchaseOrder Order(string supplier, Guid warehouse, int daysAgo, int? expectedIn, params (string Sku, decimal Qty)[] lines)
        {
            var po = new PurchaseOrder(tenantId, $"PO-{++number:D6}", suppliers[supplier].Id, warehouse, today.AddDays(-daysAgo),
                expectedIn is { } e ? today.AddDays(-daysAgo + e) : null, null);
            po.ReplaceItems(lines.Where(l => variants.ContainsKey(l.Sku)).Select(l => (variants[l.Sku].Id, l.Qty, variants[l.Sku].CostPrice)));
            db.PurchaseOrders.Add(po);
            return po;
        }

        // 1) Fully received three weeks ago.
        var received = Order("LUMEN", manila.Value, 24, 5, ("LED-E27-9W", 200), ("LED-E27-12W", 150), ("LED-T8-18-DL", 80));
        received.Submit(ownerId, now.AddDays(-24));
        received.Approve(ownerId, now.AddDays(-23));
        await ReceiveAsync(received, all: true, daysAgo: 19);

        // 2) Partially received: half of the wire arrived, the rest is back-ordered.
        var partial = Order("PHELEC", manila.Value, 12, 7, ("THHN-35-RED", 20), ("THHN-35-BLK", 20), ("CB-1P-20A", 50));
        partial.Submit(ownerId, now.AddDays(-12));
        partial.Approve(ownerId, now.AddDays(-11));
        await ReceiveAsync(partial, all: false, daysAgo: 6);

        // 3) Approved, waiting for delivery to Cebu.
        var approved = Order("AQUAFLO", cebu ?? manila.Value, 5, 4, ("PVC-BL-12", 120), ("PVC-ELB-12", 400), ("TFL-12", 300));
        approved.Submit(ownerId, now.AddDays(-5));
        approved.Approve(ownerId, now.AddDays(-4));

        // 4) Submitted, awaiting approval.
        var submitted = Order("TOOLMASTER", manila.Value, 2, 10, ("PT-DRL-12V", 10), ("HAM-16OZ", 24));
        submitted.Submit(ownerId, now.AddDays(-2));

        // 5) Draft still being prepared.
        Order("COLORWORKS", manila.Value, 0, 14, ("PNT-LTX-WHT-1G", 40), ("BRS-2IN", 100));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo purchasing: {Suppliers} suppliers, {Orders} purchase orders", suppliers.Count, number);

        async Task ReceiveAsync(PurchaseOrder po, bool all, int daysAgo)
        {
            var at = now.AddDays(-daysAgo);
            var quantities = po.Items.ToDictionary(i => i.Id, i => all ? i.QuantityOrdered : Math.Floor(i.QuantityOrdered / 2));
            po.Receive(quantities, at);

            var receipt = new PurchaseReceipt(tenantId, $"GRN-{++receiptNumber:D6}", po.Id, po.WarehouseId, null, ownerId, at);
            foreach (var item in po.Items)
            {
                var qty = quantities[item.Id];
                var stock = db.WarehouseStocks.Local.SingleOrDefault(s => s.WarehouseId == po.WarehouseId && s.ProductVariantId == item.ProductVariantId)
                    ?? await db.WarehouseStocks.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.WarehouseId == po.WarehouseId && s.ProductVariantId == item.ProductVariantId, ct);
                if (stock is null)
                {
                    stock = new WarehouseStock(tenantId, po.WarehouseId, item.ProductVariantId);
                    db.WarehouseStocks.Add(stock);
                }

                stock.Receive(qty, item.UnitCost);
                receipt.AddItem(item.Id, item.ProductVariantId, qty, item.UnitCost);
                db.InventoryTransactions.Add(new InventoryTransaction(
                    tenantId, po.WarehouseId, item.ProductVariantId, InventoryMovementType.PurchaseReceipt, qty, stock.QuantityOnHand,
                    item.UnitCost, receipt.Id, $"{po.Number} / {receipt.Number}", null, null, "PurchaseOrder", po.Id, ownerId, at));
            }

            db.PurchaseReceipts.Add(receipt);
        }
    }

    /// <summary>Keeps the PO/GRN counters ahead of the seeded numbers so the next real document is PO-000006.</summary>
    public static Task SyncSequencesAsync(AppDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO tenant_sequences (tenant_id, name, next_value)
            SELECT {tenantId}, s.name, s.value FROM (VALUES ('PO', (SELECT count(*) FROM purchase_orders WHERE tenant_id = {tenantId})),
                                                       ('GRN', (SELECT count(*) FROM purchase_receipts WHERE tenant_id = {tenantId}))) AS s(name, value)
            WHERE s.value > 0
            ON CONFLICT (tenant_id, name) DO UPDATE SET next_value = GREATEST(tenant_sequences.next_value, EXCLUDED.next_value)
            """, ct);
}
