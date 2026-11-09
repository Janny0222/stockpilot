using P = StockPilot.Domain.Identity.Permissions;

namespace StockPilot.Domain.Identity;

public sealed record SystemRoleDefinition(
    Guid Id,
    string Name,
    bool IsPlatform,
    bool RestrictToAssignedWarehouses,
    IReadOnlyList<string> Permissions);

/// <summary>
/// Built-in roles (assumption A3). IDs are fixed so they can be seeded by migrations
/// and referenced in code/tests. This class IS the permission matrix from the Phase 1 plan;
/// a unit test pins it so accidental changes fail CI.
/// </summary>
public static class SystemRoles
{
    public static readonly Guid SuperAdminId = new("01900000-0000-7000-8000-000000000001");
    public static readonly Guid OwnerId = new("01900000-0000-7000-8000-000000000002");
    public static readonly Guid ManagerId = new("01900000-0000-7000-8000-000000000003");
    public static readonly Guid InventoryStaffId = new("01900000-0000-7000-8000-000000000004");
    public static readonly Guid SalesStaffId = new("01900000-0000-7000-8000-000000000005");

    private static readonly string[] TenantPermissions =
        P.All.Where(p => !p.IsPlatform).Select(p => p.Code).ToArray();

    private static readonly string[] OwnerOnly =
        [P.Users.Manage, P.Roles.Manage, P.Company.Manage, P.Audit.View];

    public static SystemRoleDefinition SuperAdmin { get; } = new(
        SuperAdminId, "Super Admin", IsPlatform: true, RestrictToAssignedWarehouses: false,
        P.All.Where(p => p.IsPlatform).Select(p => p.Code).ToArray());

    public static SystemRoleDefinition Owner { get; } = new(
        OwnerId, "Owner", IsPlatform: false, RestrictToAssignedWarehouses: false, TenantPermissions);

    public static SystemRoleDefinition Manager { get; } = new(
        ManagerId, "Manager", IsPlatform: false, RestrictToAssignedWarehouses: false,
        TenantPermissions.Except(OwnerOnly).ToArray());

    public static SystemRoleDefinition InventoryStaff { get; } = new(
        InventoryStaffId, "Inventory Staff", IsPlatform: false, RestrictToAssignedWarehouses: true,
        [
            P.Dashboard.View, P.Products.View,
            P.Inventory.View, P.Inventory.StockIn, P.Inventory.StockOut, P.Inventory.Transfer,
            P.Warehouses.View, P.Suppliers.View,
            P.Purchases.View, P.Purchases.Receive,
            P.Sales.View, P.Sales.Fulfil,
        ]);

    public static SystemRoleDefinition SalesStaff { get; } = new(
        SalesStaffId, "Sales Staff", IsPlatform: false, RestrictToAssignedWarehouses: false,
        [
            P.Dashboard.View, P.Products.View, P.Inventory.View, P.Warehouses.View,
            P.Customers.View, P.Customers.Manage,
            P.Sales.View, P.Sales.Create, P.Sales.Fulfil,
        ]);

    public static IReadOnlyList<SystemRoleDefinition> All { get; } =
        [SuperAdmin, Owner, Manager, InventoryStaff, SalesStaff];
}
