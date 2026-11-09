using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Sales;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Code).HasMaxLength(Customer.CodeMaxLength).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
        builder.Property(c => c.ContactName).HasMaxLength(100);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Phone).HasMaxLength(40);
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.Name });
    }
}

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status, o.OrderDate });
        builder.HasIndex(o => new { o.TenantId, o.CustomerId });
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.Notes).HasMaxLength(SalesOrder.NotesMaxLength);
        builder.Property(o => o.CancelReason).HasMaxLength(300);
        builder.Property(o => o.Total).HasPrecision(18, 2);
        builder.Property(o => o.Version).IsRowVersion();

        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // Cascade so lines removed from the collection (ReplaceItems on a draft) are deleted as orphans.
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SalesOrderItemConfiguration : IEntityTypeConfiguration<SalesOrderItem>
{
    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        builder.Property(i => i.QuantityOrdered).HasPrecision(18, 3);
        builder.Property(i => i.QuantityShipped).HasPrecision(18, 3);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 4);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);
        builder.ToTable(t => t.HasCheckConstraint("ck_sales_order_items_shipped_le_ordered", "quantity_shipped <= quantity_ordered"));
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesShipmentConfiguration : IEntityTypeConfiguration<SalesShipment>
{
    public void Configure(EntityTypeBuilder<SalesShipment> builder)
    {
        builder.Property(s => s.Number).HasMaxLength(20).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.Number }).IsUnique();
        builder.HasIndex(s => s.SalesOrderId);
        builder.HasIndex(s => new { s.TenantId, s.ShippedAt });
        builder.Property(s => s.Note).HasMaxLength(300);
        builder.HasOne<SalesOrder>().WithMany().HasForeignKey(s => s.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(s => s.Items).WithOne().HasForeignKey(i => i.SalesShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SalesShipmentItemConfiguration : IEntityTypeConfiguration<SalesShipmentItem>
{
    public void Configure(EntityTypeBuilder<SalesShipmentItem> builder)
    {
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 4);
        builder.Property(i => i.UnitCost).HasPrecision(18, 4);
        builder.HasOne<SalesOrderItem>().WithMany().HasForeignKey(i => i.SalesOrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
    }
}
