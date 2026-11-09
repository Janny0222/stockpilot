import {
  Boxes,
  Building,
  ChartColumn,
  LayoutDashboard,
  Package,
  Receipt,
  Settings,
  ShieldCheck,
  ShoppingCart,
  Truck,
  Warehouse,
  type LucideIcon,
} from 'lucide-react'
import { Permissions as P } from '@/lib/permissions'

export type NavItem = {
  to: string
  label: string
  icon: LucideIcon
  /** Shown only when the user's role has this permission. */
  permission: string
  /** False until the module ships; the page then explains that instead of faking data. */
  available: boolean
}

export const tenantNav: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, permission: P.Dashboard.View, available: true },
  { to: '/products', label: 'Products', icon: Package, permission: P.Products.View, available: true },
  { to: '/inventory', label: 'Inventory', icon: Boxes, permission: P.Inventory.View, available: true },
  { to: '/purchases', label: 'Purchases', icon: ShoppingCart, permission: P.Purchases.View, available: true },
  { to: '/sales', label: 'Sales', icon: Receipt, permission: P.Sales.View, available: false },
  { to: '/warehouses', label: 'Warehouses', icon: Warehouse, permission: P.Warehouses.View, available: true },
  { to: '/suppliers', label: 'Suppliers', icon: Truck, permission: P.Suppliers.View, available: true },
  { to: '/reports', label: 'Reports', icon: ChartColumn, permission: P.Reports.View, available: false },
]

export const platformNav: NavItem[] = [
  { to: '/platform', label: 'Companies', icon: Building, permission: P.Platform.TenantsView, available: false },
]

/** Settings is visible to everyone: every user can manage their own account and sessions. */
export const settingsNav: NavItem = { to: '/settings/account', label: 'Settings', icon: Settings, permission: '', available: true }

export const platformIcon = ShieldCheck

export function visibleNav(items: NavItem[], permissions: readonly string[]) {
  return items.filter((item) => !item.permission || permissions.includes(item.permission))
}
