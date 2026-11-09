using StockPilot.Domain.Common;

namespace StockPilot.Domain.Catalog;

/// <summary>
/// A sellable, stockable unit: "THHN 3.5mm² — Red, 150 m roll". Owns the identifiers (SKU, barcode)
/// and prices. Stock levels (WarehouseStock) and every inventory movement reference the variant.
/// </summary>
public sealed class ProductVariant : TenantEntity
{
    public const int SkuMaxLength = 40;
    public const int BarcodeMaxLength = 64;
    public const int NameMaxLength = 100;

    private ProductVariant() { }

    internal ProductVariant(Guid tenantId, Guid productId, string sku, string? name, string? barcode, decimal costPrice, decimal sellingPrice, decimal reorderLevel)
        : base(tenantId)
    {
        ProductId = productId;
        IsActive = true;
        Update(sku, name, barcode, costPrice, sellingPrice, reorderLevel);
    }

    public Guid ProductId { get; private set; }

    /// <summary>Stock-keeping unit. Upper-cased, unique per company.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>Optional label distinguishing variants ("Large / Red"). Null for a single-variant product.</summary>
    public string? Name { get; private set; }

    /// <summary>EAN/UPC/internal barcode. Optional; unique per company when present.</summary>
    public string? Barcode { get; private set; }

    public decimal CostPrice { get; private set; }
    public decimal SellingPrice { get; private set; }

    /// <summary>When stock falls to this level or below, it's "low stock".</summary>
    public decimal ReorderLevel { get; private set; }

    public bool IsActive { get; private set; }

    public uint Version { get; private set; }

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    public void Update(string sku, string? name, string? barcode, decimal costPrice, decimal sellingPrice, decimal reorderLevel)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainException("SKU_REQUIRED", "SKU is required.");
        }

        if (costPrice < 0 || sellingPrice < 0)
        {
            throw new DomainException("PRICE_NEGATIVE", "Prices can't be negative.");
        }

        if (reorderLevel < 0)
        {
            throw new DomainException("REORDER_NEGATIVE", "Reorder level can't be negative.");
        }

        Sku = NormalizeSku(sku);
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        CostPrice = decimal.Round(costPrice, 4);
        SellingPrice = decimal.Round(sellingPrice, 4);
        ReorderLevel = decimal.Round(reorderLevel, 3);
    }

    internal void Deactivate() => IsActive = false;

    internal void Activate() => IsActive = true;
}
