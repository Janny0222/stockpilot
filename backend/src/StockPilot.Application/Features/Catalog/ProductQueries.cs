using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Common.Security;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Identity;
using StockPilot.Application.Common.Abstractions;

namespace StockPilot.Application.Features.Catalog;

public sealed record ProductListItemDto(
    Guid Id,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    string UnitOfMeasure,
    ProductStatus Status,
    int VariantCount,
    string? Sku,
    decimal? MinPrice,
    decimal? MaxPrice,
    Guid? PrimaryImageId,
    DateTimeOffset CreatedAt);

/// <summary>CostPrice is null when the caller may not see costs (Sales Staff).</summary>
public sealed record VariantDto(
    Guid Id, string Sku, string? Name, string? Barcode, decimal? CostPrice, decimal SellingPrice,
    decimal ReorderLevel, bool IsActive, uint Version);

public sealed record ProductImageDto(Guid Id, int Width, int Height, long SizeBytes, bool IsPrimary, int SortOrder);

public sealed record ProductDetailDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    string UnitOfMeasure,
    ProductStatus Status,
    uint Version,
    bool CanViewCost,
    IReadOnlyList<VariantDto> Variants,
    IReadOnlyList<ProductImageDto> Images,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record ListProductsQuery : PageRequest
{
    public Guid? CategoryId { get; init; }

    /// <summary>Defaults to Active. Archived products are hidden unless asked for.</summary>
    public ProductStatus? Status { get; init; }
}

public sealed class ListProductsHandler(IAppDbContext db)
{
    public async Task<PagedResult<ProductListItemDto>> Handle(ListProductsQuery query, CancellationToken ct)
    {
        var status = query.Status ?? ProductStatus.Active;
        IQueryable<Product> products = db.Products.AsNoTracking().Where(p => p.Status == status);

        if (query.CategoryId is { } categoryId)
        {
            // Selecting a parent category includes its subcategories.
            products = products.Where(p => p.CategoryId == categoryId || p.Category!.ParentId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Name, SKU or barcode. Values are bound as SQL parameters, never concatenated.
            var term = query.Search.Trim().ToLowerInvariant();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Variants.Any(v => v.Sku.ToLower().Contains(term) || (v.Barcode != null && v.Barcode.ToLower().Contains(term))));
        }

        products = query.ParseSort() switch
        {
            ("name", true) => products.OrderByDescending(p => p.Name),
            ("createdat", false) => products.OrderBy(p => p.CreatedAt),
            ("createdat", true) => products.OrderByDescending(p => p.CreatedAt),
            ("price", false) => products.OrderBy(p => p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.SellingPrice)),
            ("price", true) => products.OrderByDescending(p => p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.SellingPrice)),
            _ => products.OrderBy(p => p.Name),
        };

        return await products
            .Select(p => new ProductListItemDto(
                p.Id,
                p.Name,
                p.CategoryId,
                p.Category != null ? p.Category.Name : null,
                p.UnitOfMeasure,
                p.Status,
                p.Variants.Count(v => v.IsActive),
                p.Variants.Where(v => v.IsActive).OrderBy(v => v.CreatedAt).Select(v => v.Sku).FirstOrDefault(),
                p.Variants.Where(v => v.IsActive).Min(v => (decimal?)v.SellingPrice),
                p.Variants.Where(v => v.IsActive).Max(v => (decimal?)v.SellingPrice),
                p.Images.Where(i => i.IsPrimary).Select(i => (Guid?)i.Id).FirstOrDefault(),
                p.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }
}

public sealed class GetProductHandler(IAppDbContext db, PermissionChecker permissions)
{
    public async Task<ProductDetailDto> Handle(Guid id, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Variants)
            .Include(p => p.Images) // ≤50 variants × ≤5 images: a single joined query is fine
            .SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product", id);

        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);
        return ProductMapper.ToDetail(product, canViewCost);
    }
}

public sealed record VariantLookupDto(Guid ProductId, string ProductName, Guid VariantId, string Sku, string? VariantName, string? Barcode, decimal SellingPrice);

/// <summary>Exact SKU or barcode match, for barcode scanners and quick entry.</summary>
public sealed class LookupVariantHandler(IAppDbContext db)
{
    public async Task<VariantLookupDto> Handle(string code, CancellationToken ct)
    {
        var normalized = code.Trim();
        var sku = ProductVariant.NormalizeSku(normalized);

        return await db.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && (v.Sku == sku || v.Barcode == normalized))
            .Join(db.Products.Where(p => p.Status == ProductStatus.Active), v => v.ProductId, p => p.Id,
                (v, p) => new VariantLookupDto(p.Id, p.Name, v.Id, v.Sku, v.Name, v.Barcode, v.SellingPrice))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Product with code", code);
    }
}

internal static class ProductMapper
{
    public static ProductDetailDto ToDetail(Product p, bool canViewCost) => new(
        p.Id,
        p.Name,
        p.Description,
        p.CategoryId,
        p.Category?.Name,
        p.UnitOfMeasure,
        p.Status,
        p.Version,
        canViewCost,
        p.Variants
            .OrderByDescending(v => v.IsActive).ThenBy(v => v.CreatedAt)
            .Select(v => new VariantDto(v.Id, v.Sku, v.Name, v.Barcode, canViewCost ? v.CostPrice : null,
                v.SellingPrice, v.ReorderLevel, v.IsActive, v.Version))
            .ToList(),
        p.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto(i.Id, i.Width, i.Height, i.SizeBytes, i.IsPrimary, i.SortOrder))
            .ToList(),
        p.CreatedAt,
        p.UpdatedAt);
}
