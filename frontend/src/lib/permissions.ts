/**
 * Mirrors StockPilot.Domain/Identity/Permissions.cs. Keep in sync: these strings are the contract.
 * The frontend only uses them to decide what to SHOW; the API decides what is ALLOWED.
 */
export const Permissions = {
  Dashboard: { View: 'dashboard.view' },
  Products: { View: 'products.view', Manage: 'products.manage' },
  Inventory: {
    View: 'inventory.view',
    ViewCost: 'inventory.view_cost',
    StockIn: 'inventory.stock_in',
    StockOut: 'inventory.stock_out',
    Adjust: 'inventory.adjust',
    Transfer: 'inventory.transfer',
  },
  Warehouses: { View: 'warehouses.view', Manage: 'warehouses.manage' },
  Suppliers: { View: 'suppliers.view', Manage: 'suppliers.manage' },
  Purchases: { View: 'purchases.view', Manage: 'purchases.manage', Approve: 'purchases.approve', Receive: 'purchases.receive' },
  Customers: { View: 'customers.view', Manage: 'customers.manage' },
  Sales: { View: 'sales.view', Create: 'sales.create', Fulfil: 'sales.fulfil', Cancel: 'sales.cancel' },
  Reports: { View: 'reports.view', Export: 'reports.export' },
  Users: { Manage: 'users.manage' },
  Roles: { Manage: 'roles.manage' },
  Company: { Manage: 'company.manage' },
  Audit: { View: 'audit.view' },
  Platform: { TenantsView: 'platform.tenants.view', TenantsManage: 'platform.tenants.manage', MetricsView: 'platform.metrics.view' },
} as const
