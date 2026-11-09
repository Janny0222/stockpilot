using StockPilot.Domain.Common;

namespace StockPilot.Domain.Identity;

/// <summary>
/// A named set of permissions. TenantId null = built-in system role shared by all tenants.
/// Tenant-specific custom roles (TenantId set) arrive in Phase 5; the schema already supports them.
/// </summary>
public sealed class Role : Entity
{
    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    public Role(Guid id, Guid? tenantId, string name, bool isSystem, bool restrictToAssignedWarehouses)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        IsSystem = isSystem;
        RestrictToAssignedWarehouses = restrictToAssignedWarehouses;
    }

    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }

    /// <summary>When true, users with this role only see/act on warehouses assigned to them.</summary>
    public bool RestrictToAssignedWarehouses { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;
}

/// <summary>A row of the permission catalogue (seeded from <see cref="Identity.Permissions.All"/>).</summary>
public sealed class Permission
{
    private Permission() { }

    public Permission(string code, string group, string description)
    {
        Code = code;
        Group = group;
        Description = description;
    }

    public string Code { get; private set; } = string.Empty;
    public string Group { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
}

/// <summary>Join row: Role ⟷ Permission (many-to-many).</summary>
public sealed class RolePermission
{
    private RolePermission() { }

    public RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
}
