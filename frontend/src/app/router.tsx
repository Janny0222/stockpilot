import { Navigate, createBrowserRouter } from 'react-router'
import { AccountPage } from '@/features/account/account-page'
import { CheckEmailPage, ForgotPasswordPage, ResetPasswordPage, VerifyEmailPage } from '@/features/auth/pages/email-pages'
import { LoginPage } from '@/features/auth/pages/login-page'
import { RegisterPage } from '@/features/auth/pages/register-page'
import { DashboardPage } from '@/features/dashboard/dashboard-page'
import { CategoriesPage } from '@/features/products/pages/categories-page'
import { NewProductPage } from '@/features/products/pages/new-product-page'
import { ProductDetailPage } from '@/features/products/pages/product-detail-page'
import { ProductsPage } from '@/features/products/pages/products-page'
import { WarehouseDetailPage } from '@/features/warehouses/pages/warehouse-detail-page'
import { WarehousesPage } from '@/features/warehouses/pages/warehouses-page'
import { Permissions as P } from '@/lib/permissions'
import { useAuthStore } from '@/stores/auth-store'
import { ComingSoonPage, NotFoundPage } from './coming-soon-page'
import { RequireAuth, RequireGuest, RequirePermission } from './guards'
import { AppShell, type RouteHandle } from './layout/app-shell'

const crumb = (label: string): RouteHandle => ({ crumb: label })

/** "/" sends each role to the most useful landing page it can actually open. */
function Home() {
  const permissions = useAuthStore((s) => s.user?.permissions ?? [])
  if (permissions.includes(P.Dashboard.View)) return <DashboardPage />
  if (permissions.includes(P.Platform.TenantsView)) return <Navigate to="/platform" replace />
  return <Navigate to="/settings/account" replace />
}

const soon = (path: string, title: string, permission: string, description: string) => ({
  path,
  handle: crumb(title),
  element: (
    <RequirePermission permission={permission}>
      <ComingSoonPage title={title} description={description} />
    </RequirePermission>
  ),
})

export const router = createBrowserRouter([
  // Public pages for signed-out visitors.
  {
    element: <RequireGuest />,
    children: [
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
      { path: '/register/check-email', element: <CheckEmailPage /> },
      { path: '/forgot-password', element: <ForgotPasswordPage /> },
    ],
  },
  // Email links work whether or not someone is signed in on this browser.
  { path: '/verify-email', element: <VerifyEmailPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },

  // The application.
  {
    element: <RequireAuth />,
    children: [
      {
        path: '/',
        element: <AppShell />,
        handle: crumb('Home'),
        children: [
          { index: true, element: <Home /> },
          {
            path: 'warehouses',
            handle: crumb('Warehouses'),
            children: [
              { index: true, element: <RequirePermission permission={P.Warehouses.View}><WarehousesPage /></RequirePermission> },
              { path: ':id', handle: crumb('Details'), element: <RequirePermission permission={P.Warehouses.View}><WarehouseDetailPage /></RequirePermission> },
            ],
          },
          {
            path: 'products',
            handle: crumb('Products'),
            children: [
              { index: true, element: <RequirePermission permission={P.Products.View}><ProductsPage /></RequirePermission> },
              { path: 'new', handle: crumb('New product'), element: <RequirePermission permission={P.Products.Manage}><NewProductPage /></RequirePermission> },
              { path: 'categories', handle: crumb('Categories'), element: <RequirePermission permission={P.Products.View}><CategoriesPage /></RequirePermission> },
              { path: ':id', handle: crumb('Details'), element: <RequirePermission permission={P.Products.View}><ProductDetailPage /></RequirePermission> },
            ],
          },
          { path: 'settings/account', handle: crumb('Account & sessions'), element: <AccountPage /> },
          soon('inventory', 'Inventory', P.Inventory.View, 'Stock levels, stock-in and stock-out, adjustments, transfers and movement history.'),
          soon('purchases', 'Purchases', P.Purchases.View, 'Purchase orders from draft to approval to receiving, with partial deliveries.'),
          soon('sales', 'Sales', P.Sales.View, 'Sales orders with stock reservation and fulfilment.'),
          soon('suppliers', 'Suppliers', P.Suppliers.View, 'Supplier contacts and purchase history.'),
          soon('reports', 'Reports', P.Reports.View, 'Inventory valuation, sales, purchases and stock movement reports with CSV export.'),
          soon('platform', 'Companies', P.Platform.TenantsView, 'Registered companies, with suspend and activate controls.'),
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
])
