namespace StockPilot.Domain.Identity;

public sealed record PermissionDefinition(string Code, string Group, string Description, bool IsPlatform = false);

/// <summary>
/// Every permission in the system. Authorization checks these codes, never role names,
/// so new or custom roles need no code changes. Codes are persisted (permission table),
/// so treat them as a public contract: add new ones freely, rename only with a migration.
/// </summary>
public static class Permissions
{
    public static class Dashboard
    {
        public const string View = "dashboard.view";
    }

    public static class Products
    {
        public const string View = "products.view";
        public const string Manage = "products.manage";
    }

    public static class Inventory
    {
        public const string View = "inventory.view";
        public const string ViewCost = "inventory.view_cost";
        public const string StockIn = "inventory.stock_in";
        public const string StockOut = "inventory.stock_out";
        public const string Adjust = "inventory.adjust";
        public const string Transfer = "inventory.transfer";
    }

    public static class Warehouses
    {
        public const string View = "warehouses.view";
        public const string Manage = "warehouses.manage";
    }

    public static class Suppliers
    {
        public const string View = "suppliers.view";
        public const string Manage = "suppliers.manage";
    }

    public static class Purchases
    {
        public const string View = "purchases.view";
        public const string Manage = "purchases.manage";
        public const string Approve = "purchases.approve";
        public const string Receive = "purchases.receive";
    }

    public static class Customers
    {
        public const string View = "customers.view";
        public const string Manage = "customers.manage";
    }

    public static class Sales
    {
        public const string View = "sales.view";
        public const string Create = "sales.create";
        public const string Fulfil = "sales.fulfil";
        public const string Cancel = "sales.cancel";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    public static class Users
    {
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string Manage = "roles.manage";
    }

    public static class Company
    {
        public const string Manage = "company.manage";
    }

    public static class Audit
    {
        public const string View = "audit.view";
    }

    public static class Platform
    {
        public const string TenantsView = "platform.tenants.view";
        public const string TenantsManage = "platform.tenants.manage";
        public const string MetricsView = "platform.metrics.view";
    }

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Dashboard.View, "Dashboard", "View the dashboard"),
        new(Products.View, "Products", "View products and categories"),
        new(Products.Manage, "Products", "Create, edit and archive products, variants, categories and images"),
        new(Inventory.View, "Inventory", "View stock levels and movement history"),
        new(Inventory.ViewCost, "Inventory", "View cost prices and inventory valuation"),
        new(Inventory.StockIn, "Inventory", "Record stock-in movements"),
        new(Inventory.StockOut, "Inventory", "Record stock-out movements"),
        new(Inventory.Adjust, "Inventory", "Record stock adjustments"),
        new(Inventory.Transfer, "Inventory", "Transfer stock between warehouses"),
        new(Warehouses.View, "Warehouses", "View warehouses"),
        new(Warehouses.Manage, "Warehouses", "Create, edit and archive warehouses"),
        new(Suppliers.View, "Suppliers", "View suppliers"),
        new(Suppliers.Manage, "Suppliers", "Create, edit and archive suppliers"),
        new(Purchases.View, "Purchases", "View purchase orders"),
        new(Purchases.Manage, "Purchases", "Create, edit, submit and cancel purchase orders"),
        new(Purchases.Approve, "Purchases", "Approve purchase orders"),
        new(Purchases.Receive, "Purchases", "Receive purchased items into stock"),
        new(Customers.View, "Customers", "View customers"),
        new(Customers.Manage, "Customers", "Create and edit customers"),
        new(Sales.View, "Sales", "View sales orders"),
        new(Sales.Create, "Sales", "Create, edit and confirm sales orders"),
        new(Sales.Fulfil, "Sales", "Fulfil (ship) sales orders"),
        new(Sales.Cancel, "Sales", "Cancel sales orders"),
        new(Reports.View, "Reports", "View reports"),
        new(Reports.Export, "Reports", "Export reports to CSV"),
        new(Users.Manage, "Settings", "Invite, edit and deactivate users"),
        new(Roles.Manage, "Settings", "Manage roles and permissions"),
        new(Company.Manage, "Settings", "Manage company profile and policies"),
        new(Audit.View, "Settings", "View the audit log"),
        new(Platform.TenantsView, "Platform", "View registered companies", IsPlatform: true),
        new(Platform.TenantsManage, "Platform", "Suspend and activate companies", IsPlatform: true),
        new(Platform.MetricsView, "Platform", "View platform metrics", IsPlatform: true),
    ];
}
