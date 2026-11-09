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
        AddressLine = addressLine?.Trim();
        City = city?.Trim();
        Country = country?.Trim();
    }
}
