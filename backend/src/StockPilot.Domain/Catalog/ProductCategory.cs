using StockPilot.Domain.Common;

namespace StockPilot.Domain.Catalog;

/// <summary>
/// Groups products ("Electrical" → "Wires &amp; Cables"). At most two levels deep, which covers how
/// SMBs organise stock without turning the UI into a tree editor.
/// </summary>
public sealed class ProductCategory : TenantEntity
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;

    private ProductCategory() { }

    public ProductCategory(Guid tenantId, string name, string? description, ProductCategory? parent)
        : base(tenantId)
    {
        Rename(name);
        Description = description?.Trim();
        SetParent(parent);
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentId { get; private set; }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("CATEGORY_NAME_REQUIRED", "Category name is required.");
        }

        Name = name.Trim();
    }

    public void Update(string name, string? description, ProductCategory? parent)
    {
        Rename(name);
        Description = description?.Trim();
        SetParent(parent);
    }

    private void SetParent(ProductCategory? parent)
    {
        if (parent is null)
        {
            ParentId = null;
            return;
        }

        if (parent.Id == Id)
        {
            throw new DomainException("CATEGORY_PARENT_INVALID", "A category can't be its own parent.");
        }

        if (parent.ParentId is not null)
        {
            throw new DomainException("CATEGORY_TOO_DEEP", "Categories can only be nested one level deep.");
        }

        if (parent.TenantId != TenantId)
        {
            throw new DomainException("CATEGORY_PARENT_INVALID", "Parent category not found.");
        }

        ParentId = parent.Id;
    }
}
