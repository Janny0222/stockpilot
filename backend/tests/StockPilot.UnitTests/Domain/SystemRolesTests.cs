using StockPilot.Domain.Identity;
using P = StockPilot.Domain.Identity.Permissions;

namespace StockPilot.UnitTests.Domain;

/// <summary>
/// Pins the permission matrix from the Phase 1 plan. If someone widens a role by accident,
/// this fails in CI before it ever reaches production.
/// </summary>
public sealed class SystemRolesTests
{
    private static readonly HashSet<string> PlatformCodes = P.All.Where(p => p.IsPlatform).Select(p => p.Code).ToHashSet();

    [Fact]
    public void Permission_codes_are_unique()
    {
        P.All.Select(p => p.Code).ShouldBeUnique();
    }

    [Fact]
    public void Every_role_permission_exists_in_the_catalogue()
    {
        var catalogue = P.All.Select(p => p.Code).ToHashSet();
        foreach (var role in SystemRoles.All)
        {
            role.Permissions.ShouldAllBe(code => catalogue.Contains(code), $"role {role.Name}");
        }
    }

    [Fact]
    public void Owner_has_every_tenant_permission_and_no_platform_permission()
    {
        var expected = P.All.Where(p => !p.IsPlatform).Select(p => p.Code);
        SystemRoles.Owner.Permissions.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void Super_admin_has_only_platform_permissions()
    {
        SystemRoles.SuperAdmin.Permissions.ShouldBe(PlatformCodes, ignoreOrder: true);
    }

    [Fact]
    public void No_tenant_role_has_a_platform_permission()
    {
        foreach (var role in SystemRoles.All.Where(r => !r.IsPlatform))
        {
            role.Permissions.ShouldNotContain(code => PlatformCodes.Contains(code), $"role {role.Name}");
        }
    }

    [Theory]
    [InlineData(P.Users.Manage)]
    [InlineData(P.Roles.Manage)]
    [InlineData(P.Company.Manage)]
    [InlineData(P.Audit.View)]
    public void Manager_cannot_perform_owner_only_actions(string permission)
    {
        SystemRoles.Manager.Permissions.ShouldNotContain(permission);
    }

    [Theory]
    [InlineData(P.Inventory.StockIn, true)]
    [InlineData(P.Inventory.StockOut, true)]
    [InlineData(P.Inventory.Transfer, true)]
    [InlineData(P.Purchases.Receive, true)]
    [InlineData(P.Inventory.Adjust, false)]
    [InlineData(P.Inventory.ViewCost, false)]
    [InlineData(P.Products.Manage, false)]
    [InlineData(P.Reports.View, false)]
    public void Inventory_staff_matrix(string permission, bool granted)
    {
        SystemRoles.InventoryStaff.Permissions.Contains(permission).ShouldBe(granted);
        SystemRoles.InventoryStaff.RestrictToAssignedWarehouses.ShouldBeTrue();
    }

    [Theory]
    [InlineData(P.Sales.Create, true)]
    [InlineData(P.Sales.Fulfil, true)]
    [InlineData(P.Customers.Manage, true)]
    [InlineData(P.Sales.Cancel, false)]
    [InlineData(P.Inventory.StockOut, false)]
    [InlineData(P.Inventory.ViewCost, false)]
    [InlineData(P.Purchases.View, false)]
    public void Sales_staff_matrix(string permission, bool granted)
    {
        SystemRoles.SalesStaff.Permissions.Contains(permission).ShouldBe(granted);
    }

    [Fact]
    public void System_role_ids_are_unique()
    {
        SystemRoles.All.Select(r => r.Id).ShouldBeUnique();
    }
}
