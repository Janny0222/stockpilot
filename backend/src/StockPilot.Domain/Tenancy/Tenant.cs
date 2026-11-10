using StockPilot.Domain.Common;

namespace StockPilot.Domain.Tenancy;

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
}

/// <summary>
/// A registered company. The root of tenancy: it is NOT itself tenant-owned
/// (its Id *is* the TenantId other rows point to).
/// </summary>
public sealed class Tenant : AuditableEntity
{
    public const int NameMaxLength = 120;

    private Tenant() { }

    public static Tenant Register(string name, string slug, string currency, string timeZone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("TENANT_NAME_REQUIRED", "Company name is required.");
        }

        return new Tenant
        {
            Name = name.Trim(),
            Slug = slug,
            Currency = currency.ToUpperInvariant(),
            TimeZone = timeZone,
            Status = TenantStatus.Active,
            AllowNegativeStock = false,
        };
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }

    /// <summary>ISO 4217 code, e.g. "PHP", "USD". One currency per company (assumption A6).</summary>
    public string Currency { get; private set; } = "USD";

    /// <summary>IANA time zone, e.g. "Asia/Manila". Used for report date boundaries.</summary>
    public string TimeZone { get; private set; } = "UTC";

    /// <summary>Company policy: may stock go below zero? Default false.</summary>
    public bool AllowNegativeStock { get; private set; }

    public bool IsActive => Status == TenantStatus.Active;

    /// <summary>
    /// Company settings. Currency is passed separately: it may only change while there are no stock movements
    /// (every stored cost and price is in it), which the use case checks because it needs the database.
    /// </summary>
    public void UpdateProfile(string name, string currency, string timeZone, bool allowNegativeStock)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("TENANT_NAME_REQUIRED", "Company name is required.");
        }

        Name = name.Trim();
        Currency = currency.ToUpperInvariant();
        TimeZone = timeZone;
        AllowNegativeStock = allowNegativeStock;
    }

    public void Suspend() => Status = TenantStatus.Suspended;

    public void Activate() => Status = TenantStatus.Active;
}
