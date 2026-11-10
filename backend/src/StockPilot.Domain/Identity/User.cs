using StockPilot.Domain.Common;

namespace StockPilot.Domain.Identity;

/// <summary>
/// A person who can sign in. Belongs to exactly one tenant (assumption A1);
/// TenantId is null only for the platform Super Admin.
///
/// Why not ASP.NET Core Identity's IdentityUser? Inheriting it would make the Domain depend on
/// an ASP.NET package and drag in ~7 AspNet* tables with a global role model that clashes with
/// per-tenant roles. We reuse Identity's battle-tested PasswordHasher (in Infrastructure) and keep
/// the user model here, small and explicit.
/// </summary>
public sealed class User : AuditableEntity
{
    public const int EmailMaxLength = 256;
    public const int NameMaxLength = 100;

    private User() { }

    public static User CreateTenantUser(Guid tenantId, Guid roleId, string email, string firstName, string lastName)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required for tenant users.", nameof(tenantId));
        }

        return Create(tenantId, roleId, email, firstName, lastName);
    }

    public static User CreatePlatformAdmin(string email, string firstName, string lastName) =>
        Create(tenantId: null, SystemRoles.SuperAdminId, email, firstName, lastName);

    private static User Create(Guid? tenantId, Guid roleId, string email, string firstName, string lastName) => new()
    {
        TenantId = tenantId,
        RoleId = roleId,
        Email = NormalizeEmail(email),
        FirstName = firstName.Trim(),
        LastName = lastName.Trim(),
        SecurityStamp = NewStamp(),
        IsActive = true,
    };

    public Guid? TenantId { get; private set; }
    public Guid RoleId { get; private set; }
    public Role Role { get; private set; } = null!;

    /// <summary>Stored lower-cased and trimmed; globally unique.</summary>
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Random value embedded in access tokens. Changing it invalidates every token issued before
    /// (password change, "log out everywhere", deactivation).
    /// </summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool IsPlatformAdmin => TenantId is null;

    /// <summary>Invited but hasn't accepted yet: no password has ever been set.</summary>
    public bool IsInvitationPending => PasswordHash is null;
    public string FullName => $"{FirstName} {LastName}".Trim();

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        RotateSecurityStamp();
    }

    /// <summary>
    /// Transparent re-hash after a successful login when the hashing algorithm/work factor was upgraded.
    /// Same password, so existing sessions stay valid (no stamp rotation).
    /// </summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void ConfirmEmail() => EmailConfirmed = true;

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public void RecordFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts)
        {
            LockoutEnd = now.Add(lockoutDuration);
            AccessFailedCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void RotateSecurityStamp() => SecurityStamp = NewStamp();

    public void Deactivate()
    {
        IsActive = false;
        RotateSecurityStamp();
    }

    public void Activate() => IsActive = true;

    public void Rename(string firstName, string lastName)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
    }

    public void ChangeRole(Guid roleId)
    {
        RoleId = roleId;
        RotateSecurityStamp();
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
