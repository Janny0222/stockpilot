import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'
import { LocationProbe } from '@/test/render'
import { makeUser } from '@/test/server'
import { useAuthStore } from '@/stores/auth-store'
import { RequireAuth, RequirePermission, safeReturnTo } from './guards'
import { tenantNav, visibleNav } from './navigation'

function renderProtected(url: string) {
  const router = createMemoryRouter(
    [
      { element: <RequireAuth />, children: [{ path: '/warehouses', element: <p>Warehouses list</p> }] },
      { path: '*', element: <LocationProbe /> },
    ],
    { initialEntries: [url] },
  )
  render(<RouterProvider router={router} />)
}

describe('RequireAuth', () => {
  it('sends a signed-out visitor to login and remembers where they were going', () => {
    useAuthStore.setState({ status: 'unauthenticated', endedBy: 'expired' })
    renderProtected('/warehouses?q=mnl')
    expect(screen.getByTestId('location')).toHaveTextContent('/login?returnTo=%2Fwarehouses%3Fq%3Dmnl')
  })

  it('does not carry the previous page over after a deliberate sign-out', () => {
    useAuthStore.setState({ status: 'unauthenticated', endedBy: 'signout' })
    renderProtected('/warehouses')
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/login$/)
  })

  it('renders the page for a signed-in user', () => {
    useAuthStore.setState({ status: 'authenticated', user: makeUser() })
    renderProtected('/warehouses')
    expect(screen.getByText('Warehouses list')).toBeInTheDocument()
  })
})

describe('RequirePermission', () => {
  it('shows an explanation instead of the page when the role lacks the permission', () => {
    useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions: ['dashboard.view'] }) })
    const router = createMemoryRouter([{ path: '/', element: <RequirePermission permission="reports.view"><p>Reports</p></RequirePermission> }])
    render(<RouterProvider router={router} />)

    expect(screen.queryByText('Reports')).not.toBeInTheDocument()
    expect(screen.getByText("You don't have access to this page")).toBeInTheDocument()
  })
})

describe('safeReturnTo', () => {
  it.each([
    ['/warehouses?q=1', '/warehouses?q=1'],
    ['//evil.example', '/'],
    ['https://evil.example', '/'],
    [null, '/'],
  ])('%s → %s', (input, expected) => {
    expect(safeReturnTo(input)).toBe(expected)
  })
})

describe('role-based navigation', () => {
  const labels = (permissions: string[]) => visibleNav(tenantNav, permissions).map((n) => n.label)

  it('Sales Staff see selling-related modules only', () => {
    const sales = ['dashboard.view', 'products.view', 'inventory.view', 'warehouses.view', 'customers.view', 'customers.manage', 'sales.view', 'sales.create', 'sales.fulfil']
    expect(labels(sales)).toEqual(['Dashboard', 'Products', 'Inventory', 'Sales', 'Warehouses'])
  })

  it('a user with no permissions sees no modules', () => {
    expect(labels([])).toEqual([])
  })
})
