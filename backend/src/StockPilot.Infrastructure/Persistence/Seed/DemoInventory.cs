using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockPilot.Domain.Inventory;

namespace StockPilot.Infrastructure.Persistence.Seed;

/// <summary>
/// Opening stock for the demo company across its three warehouses. Quantities are derived from each SKU
/// (deterministic: every environment gets the same numbers), and about one in six items is deliberately low
/// or out of stock, so low-stock screens have something to show. Every unit enters through a ledger row
/// ("Opening balance"), so the ledger reconciles with the balances from day one.
/// </summary>
internal static class DemoInventory
{
    public static async Task SeedAsync(AppDbContext db, Guid tenantId, Guid? performedBy, ILogger logger, CancellationToken ct)
    {
        if (await db.InventoryTransactions.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId, ct))
        {
            return;
        }

        var warehouses = await db.Warehouses.IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .ToDictionaryAsync(w => w.Code, w => w.Id, ct);
        var variants = await db.ProductVariants.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId && v.IsActive)
            .OrderBy(v => v.Sku)
            .Select(v => new { v.Id, v.Sku, v.CostPrice, v.ReorderLevel })
            .ToListAsync(ct);

        if (variants.Count == 0 || !warehouses.TryGetValue("MNL", out var manila))
        {
            return;
        }

        var openedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var correlationId = Guid.CreateVersion7();
        var rows = 0;

        foreach (var v in variants)
        {
            var h = StableHash(v.Sku);
            var reorder = Math.Max(1m, v.ReorderLevel);

            // Manila (main): everything, mostly healthy; every 7th item low, every 13th out of stock.
            var manilaQty = h % 13 == 0 ? 0m : h % 7 == 0 ? Math.Max(1m, reorder - 2) : reorder * (2 + h % 5);
            rows += Add(manila, manilaQty);

            // Cebu: about two thirds of the range.
            if (warehouses.TryGetValue("CEB", out var cebu) && h % 3 != 0)
            {
                rows += Add(cebu, Math.Ceiling(reorder * (1 + h % 3)));
            }

            // Davao: a small satellite store with a few fast movers.
            if (warehouses.TryGetValue("DVO", out var davao) && h % 4 == 0)
            {
                rows += Add(davao, Math.Ceiling(reorder / 2m) + h % 5);
            }

            int Add(Guid warehouseId, decimal quantity)
            {
                var stock = new WarehouseStock(tenantId, warehouseId, v.Id);
                db.WarehouseStocks.Add(stock);
                if (quantity <= 0)
                {
                    return 1;
                }

                stock.Receive(quantity, v.CostPrice);
                db.InventoryTransactions.Add(new InventoryTransaction(
                    tenantId, warehouseId, v.Id, InventoryMovementType.StockIn, quantity, stock.QuantityOnHand, v.CostPrice,
                    correlationId, "OPENING", "Opening balance", null, null, null, performedBy, openedAt));
                return 1;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo inventory: {Rows} stock rows across {Warehouses} warehouses", rows, warehouses.Count);
    }

    /// <summary>Same input → same number on every machine (string.GetHashCode is randomised per process).</summary>
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return hash & 0x7FFFFFFF;
        }
    }
}
