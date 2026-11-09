using StockPilot.Domain.Common;

namespace StockPilot.Domain.Catalog;

public enum ProductStatus
{
    Active = 1,
    Archived = 2,
}

/// <summary>Units a product is counted in. Stored as text; quantities are decimal so kg/L work.</summary>
public static class UnitsOfMeasure
{
    public static readonly IReadOnlyList<string> All = ["pcs", "box", "pack", "set", "pair", "roll", "kg", "g", "L", "mL", "m", "cm"];

    public static bool IsValid(string unit) => All.Contains(unit, StringComparer.Ordinal);
}

/// <summary>
/// The catalogue entry customers recognise ("Copper THHN Wire 3.5mm²"). The things that are actually
/// stocked, priced and sold are its <see cref="ProductVariant"/>s; every product has at least one
/// (assumption A4), so stock code never branches on "has variants".
///
/// Products are archived, never deleted: once stock moves, history must keep pointing at them.
/// </summary>
public sealed class Product : TenantEntity
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 2000;
    public const int MaxImages = 5;

    private readonly List<ProductVariant> _variants = [];
    private readonly List<ProductImage> _images = [];

    private Product() { }

    public Product(Guid tenantId, string name, string? description, Guid? categoryId, string unitOfMeasure)
        : base(tenantId)
    {
        UpdateDetails(name, description, categoryId, unitOfMeasure);
        Status = ProductStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? CategoryId { get; private set; }
    public ProductCategory? Category { get; private set; }
    public string UnitOfMeasure { get; private set; } = "pcs";
    public ProductStatus Status { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin). A stale edit → 409 instead of silently overwriting.</summary>
    public uint Version { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants;
    public IReadOnlyCollection<ProductImage> Images => _images;

    public bool IsArchived => Status == ProductStatus.Archived;

    public void UpdateDetails(string name, string? description, Guid? categoryId, string unitOfMeasure)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("PRODUCT_NAME_REQUIRED", "Product name is required.");
        }

        if (!UnitsOfMeasure.IsValid(unitOfMeasure))
        {
            throw new DomainException("UNIT_INVALID", $"Unit '{unitOfMeasure}' is not supported.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CategoryId = categoryId;
        UnitOfMeasure = unitOfMeasure;
    }

    public ProductVariant AddVariant(string sku, string? name, string? barcode, decimal costPrice, decimal sellingPrice, decimal reorderLevel)
    {
        EnsureNotArchived();

        var normalized = ProductVariant.NormalizeSku(sku);
        if (_variants.Any(v => v.Sku == normalized))
        {
            throw new DomainException("SKU_DUPLICATE", $"SKU '{normalized}' is used twice in this product.");
        }

        var variant = new ProductVariant(TenantId, Id, sku, name, barcode, costPrice, sellingPrice, reorderLevel);
        _variants.Add(variant);
        return variant;
    }

    public void ArchiveVariant(Guid variantId)
    {
        var variant = FindVariant(variantId);
        if (variant.IsActive && _variants.Count(v => v.IsActive) == 1)
        {
            throw new DomainException("LAST_VARIANT", "A product needs at least one active variant. Archive the product instead.");
        }

        variant.Deactivate();
    }

    public void RestoreVariant(Guid variantId)
    {
        EnsureNotArchived();
        FindVariant(variantId).Activate();
    }

    public void Archive() => Status = ProductStatus.Archived;

    public void Restore() => Status = ProductStatus.Active;

    public ProductImage AddImage(string storageKey, string contentType, long sizeBytes, int width, int height)
    {
        if (_images.Count >= MaxImages)
        {
            throw new DomainException("TOO_MANY_IMAGES", $"A product can have at most {MaxImages} images.");
        }

        var image = new ProductImage(TenantId, Id, storageKey, contentType, sizeBytes, width, height,
            isPrimary: _images.Count == 0, sortOrder: _images.Count);
        _images.Add(image);
        return image;
    }

    public ProductImage RemoveImage(Guid imageId)
    {
        var image = _images.SingleOrDefault(i => i.Id == imageId)
            ?? throw new DomainException("IMAGE_NOT_FOUND", "Image not found.");
        _images.Remove(image);

        if (image.IsPrimary && _images.Count > 0)
        {
            _images.OrderBy(i => i.SortOrder).First().MarkPrimary(true);
        }

        return image;
    }

    public void SetPrimaryImage(Guid imageId)
    {
        if (_images.All(i => i.Id != imageId))
        {
            throw new DomainException("IMAGE_NOT_FOUND", "Image not found.");
        }

        foreach (var image in _images)
        {
            image.MarkPrimary(image.Id == imageId);
        }
    }

    public ProductVariant FindVariant(Guid variantId) =>
        _variants.SingleOrDefault(v => v.Id == variantId)
        ?? throw new DomainException("VARIANT_NOT_FOUND", "Variant not found.");

    private void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new DomainException("PRODUCT_ARCHIVED", "Restore the product before changing its variants.");
        }
    }
}
