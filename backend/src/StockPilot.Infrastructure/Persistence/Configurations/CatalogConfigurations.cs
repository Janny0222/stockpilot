using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockPilot.Domain.Catalog;

namespace StockPilot.Infrastructure.Persistence.Configurations;

internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(ProductCategory.NameMaxLength).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(ProductCategory.DescriptionMaxLength);

        // Unique name among siblings. NULLS NOT DISTINCT (PostgreSQL 15+) makes two top-level
        // categories with the same name collide too (by default NULL parent_ids never compare equal).
        builder.HasIndex(c => new { c.TenantId, c.ParentId, c.Name }).IsUnique().AreNullsDistinct(false);

        builder.HasOne<ProductCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(p => p.UnitOfMeasure).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Version).IsRowVersion();

        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);

        // List page: filter by status, sort by name / newest.
        builder.HasIndex(p => new { p.TenantId, p.Status, p.Name });
        builder.HasIndex(p => new { p.TenantId, p.CreatedAt });
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.Property(v => v.Sku).HasMaxLength(ProductVariant.SkuMaxLength).IsRequired();
        builder.Property(v => v.Name).HasMaxLength(ProductVariant.NameMaxLength);
        builder.Property(v => v.Barcode).HasMaxLength(ProductVariant.BarcodeMaxLength);

        // Money: numeric(18,4). Quantities: numeric(18,3). Never float/double for either:
        // binary floating point can't represent 0.1 exactly, and totals drift.
        builder.Property(v => v.CostPrice).HasPrecision(18, 4);
        builder.Property(v => v.SellingPrice).HasPrecision(18, 4);
        builder.Property(v => v.ReorderLevel).HasPrecision(18, 3);
        builder.Property(v => v.Version).IsRowVersion();

        // Unique per company. Barcode is optional: PostgreSQL treats NULLs as distinct by default,
        // so many variants without a barcode don't collide.
        builder.HasIndex(v => new { v.TenantId, v.Sku }).IsUnique();
        builder.HasIndex(v => new { v.TenantId, v.Barcode }).IsUnique();
        builder.HasIndex(v => v.ProductId);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.Property(i => i.StorageKey).HasMaxLength(80).IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(50).IsRequired();
        builder.HasIndex(i => i.ProductId);
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.HasKey(f => f.Key);
        builder.Property(f => f.Key).HasMaxLength(80);
        builder.Property(f => f.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(f => f.Data).IsRequired();
        builder.HasIndex(f => f.TenantId);
    }
}
