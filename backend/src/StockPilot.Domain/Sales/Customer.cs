using StockPilot.Domain.Common;

namespace StockPilot.Domain.Sales;

/// <summary>A company or person you sell to. Archived, never deleted: sales orders keep pointing at it.</summary>
public sealed class Customer : TenantEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    private Customer() { }

    public Customer(Guid tenantId, string code, string name)
        : base(tenantId)
    {
        Update(code, name, null, null, null, null, 0, null);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }

    /// <summary>Days after delivery payment is due (0 = cash).</summary>
    public int PaymentTermsDays { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string code, string name, string? contactName, string? email, string? phone, string? address, int paymentTermsDays, string? notes)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("CUSTOMER_REQUIRED", "Customer code and name are required.");
        }

        if (paymentTermsDays is < 0 or > 365)
        {
            throw new DomainException("PAYMENT_TERMS_INVALID", "Payment terms must be between 0 and 365 days.");
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        ContactName = Clean(contactName);
        Email = Clean(email)?.ToLowerInvariant();
        Phone = Clean(phone);
        Address = Clean(address);
        PaymentTermsDays = paymentTermsDays;
        Notes = Clean(notes);
    }

    public void Archive() => IsActive = false;

    public void Restore() => IsActive = true;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
