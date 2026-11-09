using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Purchasing;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Infrastructure.Persistence.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.Code).HasMaxLength(Supplier.CodeMaxLength).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(Supplier.NameMaxLength).IsRequired();
        builder.Property(s => s.ContactName).HasMaxLength(100);
        builder.Property(s => s.Email).HasMaxLength(256);
        builder.Property(s => s.Phone).HasMaxLength(40);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.HasIndex(s => new { s.TenantId, s.Code }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.Name });
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Property(p => p.Number).HasMaxLength(20).IsRequired();
        builder.HasIndex(p => new { p.TenantId, p.Number }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Status, p.OrderDate });
        builder.HasIndex(p => new { p.TenantId, p.SupplierId });
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Notes).HasMaxLength(PurchaseOrder.NotesMaxLength);
        builder.Property(p => p.CancelReason).HasMaxLength(300);
        builder.Property(p => p.Total).HasPrecision(18, 2);
        builder.Property(p => p.Version).IsRowVersion();

        builder.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // Cascade so lines removed from the collection (ReplaceItems on a draft) are deleted as orphans.
        builder.HasMany(p => p.Items).WithOne().HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.Property(i => i.QuantityOrdered).HasPrecision(18, 3);
        builder.Property(i => i.QuantityReceived).HasPrecision(18, 3);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);
        builder.ToTable(t => t.HasCheckConstraint("ck_purchase_order_items_received_le_ordered", "quantity_received <= quantity_ordered"));
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.Property(r => r.Number).HasMaxLength(20).IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.Number }).IsUnique();
        builder.HasIndex(r => r.PurchaseOrderId);
        builder.Property(r => r.Note).HasMaxLength(300);
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(r => r.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.PurchaseReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PurchaseReceiptItemConfiguration : IEntityTypeConfiguration<PurchaseReceiptItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptItem> builder)
    {
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.HasOne<PurchaseOrderItem>().WithMany().HasForeignKey(i => i.PurchaseOrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
