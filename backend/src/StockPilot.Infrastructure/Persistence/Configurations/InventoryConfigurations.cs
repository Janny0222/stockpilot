using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Tenancy;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseStockConfiguration : IEntityTypeConfiguration<WarehouseStock>
{
    public void Configure(EntityTypeBuilder<WarehouseStock> builder)
    {
        builder.ToTable("warehouse_stocks", t =>
        {
            // Defence in depth: even a buggy code path can't persist a negative reservation.
            t.HasCheckConstraint("ck_warehouse_stocks_reserved_non_negative", "quantity_reserved >= 0");
        });

        builder.Property(s => s.QuantityOnHand).HasPrecision(18, 3);
        builder.Property(s => s.QuantityReserved).HasPrecision(18, 3);
        builder.Property(s => s.AverageUnitCost).HasPrecision(18, 4);
        builder.Property(s => s.Version).IsRowVersion();

        // One balance row per (warehouse, variant). StockLocker's INSERT … ON CONFLICT relies on this index.
        builder.HasIndex(s => new { s.WarehouseId, s.ProductVariantId }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.ProductVariantId });

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(s => s.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("inventory_transactions");
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.QuantityChange).HasPrecision(18, 3);
        builder.Property(t => t.QuantityAfter).HasPrecision(18, 3);
        builder.Property(t => t.UnitCost).HasPrecision(18, 4);
        builder.Property(t => t.Reference).HasMaxLength(InventoryTransaction.ReferenceMaxLength);
        builder.Property(t => t.Note).HasMaxLength(InventoryTransaction.NoteMaxLength);
        builder.Property(t => t.SourceType).HasMaxLength(40);

        // History screens: newest first, per company / warehouse / variant.
        builder.HasIndex(t => new { t.TenantId, t.OccurredAt });
        builder.HasIndex(t => new { t.TenantId, t.WarehouseId, t.OccurredAt });
        builder.HasIndex(t => new { t.TenantId, t.ProductVariantId, t.OccurredAt });
        builder.HasIndex(t => t.CorrelationId);

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(t => t.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(t => t.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.Property(t => t.Number).HasMaxLength(20).IsRequired();
        builder.HasIndex(t => new { t.TenantId, t.Number }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.CompletedAt });
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Note).HasMaxLength(StockTransfer.NoteMaxLength);

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(t => t.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(t => t.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Items).WithOne().HasForeignKey(i => i.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockTransferItemConfiguration : IEntityTypeConfiguration<StockTransferItem>
{
    public void Configure(EntityTypeBuilder<StockTransferItem> builder)
    {
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(r => new { r.TenantId, r.Key });
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
        // text, not jsonb: jsonb re-orders keys and strips whitespace, but a replay must return the
        // byte-for-byte identical response the client would have received the first time.
        builder.Property(r => r.ResponseBody).HasColumnType("text");
        builder.HasIndex(r => r.CreatedAt); // for the cleanup job
    }
}

/// <summary>Counter row per (company, document type). Only touched by DocumentNumberGenerator's atomic upsert.</summary>
internal sealed class TenantSequence
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long NextValue { get; set; }
}

internal sealed class TenantSequenceConfiguration : IEntityTypeConfiguration<TenantSequence>
{
    public void Configure(EntityTypeBuilder<TenantSequence> builder)
    {
        builder.ToTable("tenant_sequences");
        builder.HasKey(s => new { s.TenantId, s.Name });
        builder.Property(s => s.Name).HasMaxLength(20);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}
