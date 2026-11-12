/**
 * Where a record lives in the app. The API refers to records as (entityType, id), for audit entries and notification
 * targets, so the frontend owns its own URLs and the backend never has to know them.
 */
const PAGES: Record<string, (id: string) => string> = {
  User: (id) => `/settings/users/${id}`,
  Product: (id) => `/products/${id}`,
  PurchaseOrder: (id) => `/purchases/${id}`,
  SalesOrder: (id) => `/sales/${id}`,
  Customer: (id) => `/customers/${id}`,
  Supplier: (id) => `/suppliers/${id}`,
  Warehouse: (id) => `/warehouses/${id}`,
  StockTransfer: (id) => `/inventory/transfers/${id}`,
  /** A low-stock alert points at the warehouse's stock list, filtered to low items. */
  LowStock: (warehouseId) => `/inventory?warehouse=${warehouseId}&low=1`,
}

export function entityPath(entityType: string | null | undefined, id: string | null | undefined) {
  if (!entityType || !id) return null
  // IDs are server-generated GUIDs, but encode anyway: a value like "../settings" or "x&y=" must never steer routing.
  return PAGES[entityType]?.(encodeURIComponent(id)) ?? null
}
