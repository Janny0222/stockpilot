using StockPilot.Domain.Common;

namespace StockPilot.Domain.Warehousing;

/// <summary>
/// A physical stock location. Phase 2 only needs the basics (a default warehouse is created at
/// company registration, and it is the first tenant-owned resource used to prove isolation).
/// Full management (address, archive, stock views) lands in the Warehouses module.
/// </summary>
public sealed class Warehouse : TenantEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 120;

    private Warehouse() { }

    public Warehouse(Guid tenantId, string code, string name, bool isDefault)
        : base(tenantId)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("WAREHOUSE_CODE_REQUIRED", "Warehouse code is required.");
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsDefault = isDefault;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? AddressLine { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public void SetAddress(string? addressLine, string? city, string? country)
    {
        AddressLine = string.IsNullOrWhiteSpace(addressLine) ? null : addressLine.Trim();
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim();
    }

    public void Update(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("WAREHOUSE_CODE_REQUIRED", "Warehouse code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("WAREHOUSE_NAME_REQUIRED", "Warehouse name is required.");
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    /// <summary>The caller checks that no stock remains: archived warehouses can't hold or receive stock.</summary>
    public void Archive()
    {
        if (IsDefault)
        {
            throw new DomainException("WAREHOUSE_IS_DEFAULT", "Make another warehouse the default before archiving this one.");
        }

        IsActive = false;
    }

    public void Restore() => IsActive = true;

    internal void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Exactly one default warehouse per company: switch it on one and off on all the others.</summary>
    public static void MakeDefault(Warehouse target, IEnumerable<Warehouse> all)
    {
        if (!target.IsActive)
        {
            throw new DomainException("WAREHOUSE_ARCHIVED", "Restore the warehouse before making it the default.");
        }

        foreach (var warehouse in all)
        {
            warehouse.SetDefault(warehouse.Id == target.Id);
        }
    }
}
