using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Inventory;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.Infrastructure.Inventory;

internal sealed class StockLocker(AppDbContext db) : IStockLocker
{
    public async Task<IReadOnlyDictionary<(Guid WarehouseId, Guid VariantId), WarehouseStock>> LockAsync(
        Guid tenantId, IReadOnlyCollection<(Guid WarehouseId, Guid VariantId)> keys, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Stock rows must be locked inside a transaction (ExecuteInTransactionAsync).");
        }

        var distinct = keys.Distinct().ToArray();
        var warehouseIds = distinct.Select(k => k.WarehouseId).ToArray();
        var variantIds = distinct.Select(k => k.VariantId).ToArray();
        var newIds = distinct.Select(_ => Guid.CreateVersion7()).ToArray();

        // 1) Make sure a balance row exists for every pair. ON CONFLICT DO NOTHING makes this safe when two
        //    requests create the first stock of the same item at the same time: one inserts, the other no-ops.
        //    All values are bound parameters (FormattableString → NpgsqlParameter), never concatenated.
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO warehouse_stocks (id, tenant_id, warehouse_id, product_variant_id, quantity_on_hand, quantity_reserved, average_unit_cost, created_at)
            SELECT k.id, {tenantId}, k.warehouse_id, k.variant_id, 0, 0, 0, now()
            FROM unnest({newIds}, {warehouseIds}, {variantIds}) AS k(id, warehouse_id, variant_id)
            ON CONFLICT (warehouse_id, product_variant_id) DO NOTHING
            """,
            cancellationToken);

        // 2) Lock them, ALWAYS in id order, so concurrent operations queue instead of deadlocking.
        //    Another transaction touching the same rows now waits here until we commit or roll back.
        var lockedIds = await db.Database.SqlQuery<Guid>(
            $"""
            SELECT s.id AS "Value"
            FROM warehouse_stocks s
            JOIN unnest({warehouseIds}, {variantIds}) AS k(warehouse_id, variant_id)
              ON s.warehouse_id = k.warehouse_id AND s.product_variant_id = k.variant_id
            WHERE s.tenant_id = {tenantId}
            ORDER BY s.id
            FOR UPDATE OF s
            """).ToListAsync(cancellationToken);

        // 3) Load as tracked entities. The rows are already locked by THIS transaction, so the values read
        //    here are current and can't change under us until commit.
        var rows = await db.WarehouseStocks
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && lockedIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => (r.WarehouseId, r.ProductVariantId));
    }
}

internal sealed class DocumentNumberGenerator(AppDbContext db) : IDocumentNumberGenerator
{
    public async Task<string> NextAsync(Guid tenantId, string prefix, CancellationToken cancellationToken)
    {
        // Atomic "insert 1 or increment" in a single statement. The row stays locked until the surrounding
        // transaction commits, so two concurrent transfers can't get the same number. If the transaction rolls
        // back, the increment rolls back too (no gaps from failed operations).
        var values = await db.Database.SqlQuery<long>(
            $"""
            INSERT INTO tenant_sequences (tenant_id, name, next_value) VALUES ({tenantId}, {prefix}, 1)
            ON CONFLICT (tenant_id, name) DO UPDATE SET next_value = tenant_sequences.next_value + 1
            RETURNING next_value AS "Value"
            """).ToListAsync(cancellationToken);

        return $"{prefix}-{values.Single():D6}";
    }
}
