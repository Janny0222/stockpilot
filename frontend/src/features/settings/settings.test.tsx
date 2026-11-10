import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { AcceptInvitationPage } from '@/features/auth/pages/accept-invitation-page'
import type { AuditLogDto, CompanySettingsDto, RolesOverviewDto, UserDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, problem, server } from '@/test/server'
import { AuditLogPage } from './audit-log-page'
import { CompanySettingsPage } from './company-page'
import { SettingsLayout } from './settings-layout'
import { UserDetailPage, UsersPage } from './users-pages'

const OWNER = 'role-owner'
const STAFF = 'role-inv'

const roles: RolesOverviewDto = {
  roles: [
    { id: OWNER, name: 'Owner', restrictToAssignedWarehouses: false, isSystem: true, permissions: ['users.manage', 'products.view'] },
    { id: STAFF, name: 'Inventory Staff', restrictToAssignedWarehouses: true, isSystem: true, permissions: ['inventory.view'] },
  ],
  permissions: [
    { code: 'products.view', group: 'Products', description: 'View products and categories' },
    { code: 'inventory.view', group: 'Inventory', description: 'View stock levels and movement history' },
    { code: 'users.manage', group: 'Settings', description: 'Invite, edit and deactivate users' },
  ],
}

const person = (over: Partial<UserDto> = {}): UserDto => ({
  id: 'u2',
  email: 'ivy@acme.test',
  firstName: 'Ivy',
  lastName: 'Reyes',
  roleId: STAFF,
  roleName: 'Inventory Staff',
  restrictToAssignedWarehouses: true,
  status: 'Active',
  lastLoginAt: null,
  createdAt: '2026-11-01T00:00:00Z',
  warehouses: [{ id: 'w2', code: 'CEB', name: 'Cebu' }],
  isYou: false,
  ...over,
})

const warehouses = {
  items: [
    { id: 'w1', code: 'MNL', name: 'Manila', addressLine: null, city: null, country: null, isDefault: true, isActive: true },
    { id: 'w2', code: 'CEB', name: 'Cebu', addressLine: null, city: null, country: null, isDefault: false, isActive: true },
  ],
  page: 1,
  pageSize: 100,
  totalCount: 2,
}

const as = (...permissions: string[]) => useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions }) })

beforeEach(() => {
  server.use(
    http.get(apiUrl('/roles'), () => HttpResponse.json(roles)),
    http.get(apiUrl('/warehouses'), () => HttpResponse.json(warehouses)),
  )
})

describe('Settings navigation', () => {
  it('shows company-wide sections only to roles that can open them', () => {
    as('dashboard.view')
    const { unmount } = renderRoute(<SettingsLayout />, { path: '/settings/account' })
    expect(screen.queryByRole('navigation', { name: 'Settings sections' })).not.toBeInTheDocument()
    unmount()

    as('users.manage', 'company.manage', 'audit.view')
    renderRoute(<SettingsLayout />, { path: '/settings/account' })
    const nav = screen.getByRole('navigation', { name: 'Settings sections' })
    expect(
      within(nav)
        .getAllByRole('link')
        .map((l) => l.textContent),
    ).toEqual(['Your account', 'Company', 'Users', 'Audit log'])
  })
})

describe('Users: inviting', () => {
  beforeEach(() => {
    as('users.manage')
    server.use(http.get(apiUrl('/users'), () => HttpResponse.json({ items: [person()], page: 1, pageSize: 20, totalCount: 1 })))
  })

  it('asks for a warehouse for warehouse-scoped roles, then sends the invitation', async () => {
    let body: Record<string, unknown> | null = null
    server.use(
      http.post(apiUrl('/users'), async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(person({ id: 'u3', status: 'Invited', email: 'new@acme.test' }), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<UsersPage />, { path: '/settings/users' })

    expect(await screen.findByText('CEB')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Invite user/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Email'), 'new@acme.test')
    await user.type(within(dialog).getByLabelText('First name'), 'Nina')
    await user.type(within(dialog).getByLabelText('Last name'), 'Cruz')

    await user.click(within(dialog).getByRole('combobox', { name: 'Role' }))
    await user.click(await screen.findByRole('option', { name: /Inventory Staff/ }))

    // The warehouse picker appears only for warehouse-scoped roles, and one is required.
    await user.click(within(dialog).getByRole('button', { name: /Send invitation/ }))
    expect(await within(dialog).findByText(/Pick at least one warehouse/)).toBeInTheDocument()
    expect(body).toBeNull()

    await user.click(within(dialog).getByRole('checkbox', { name: /Cebu/ }))
    await user.click(within(dialog).getByRole('button', { name: /Send invitation/ }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/settings/users/u3')
    expect(body).toEqual({ email: 'new@acme.test', firstName: 'Nina', lastName: 'Cruz', roleId: STAFF, warehouseIds: ['w2'] })
  })

  it('shows "email taken" on the email field', async () => {
    server.use(http.post(apiUrl('/users'), () => problem(409, 'EMAIL_TAKEN', 'An account with this email already exists.')))
    const user = userEvent.setup()
    renderRoute(<UsersPage />, { path: '/settings/users' })

    await user.click(await screen.findByRole('button', { name: /Invite user/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Email'), 'taken@acme.test')
    await user.type(within(dialog).getByLabelText('First name'), 'T')
    await user.type(within(dialog).getByLabelText('Last name'), 'K')
    await user.click(within(dialog).getByRole('combobox', { name: 'Role' }))
    await user.click(await screen.findByRole('option', { name: /Owner/ }))
    await user.click(within(dialog).getByRole('button', { name: /Send invitation/ }))

    const email = within(dialog).getByLabelText('Email')
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'))
    expect(within(dialog).getByText('An account with this email already exists.')).toBeInTheDocument()
  })
})

describe('Users: one person', () => {
  beforeEach(() => as('users.manage'))

  it('deactivates someone after confirming', async () => {
    let deactivated = false
    server.use(
      http.get(apiUrl('/users/u2'), () => HttpResponse.json(person({ status: deactivated ? 'Deactivated' : 'Active' }))),
      http.post(apiUrl('/users/u2/deactivate'), () => {
        deactivated = true
        return new HttpResponse(null, { status: 204 })
      }),
      http.get(apiUrl('/audit-logs'), () => HttpResponse.json({ items: [], page: 1, pageSize: 25, totalCount: 0 })),
    )
    const user = userEvent.setup()
    renderRoute(<UserDetailPage />, { path: '/settings/users/:id', url: '/settings/users/u2' })

    // Warehouse scope is pre-filled from the server.
    expect(await screen.findByRole('checkbox', { name: /Cebu/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Manila/ })).not.toBeChecked()

    await user.click(screen.getByRole('button', { name: /Deactivate/ }))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Deactivate' }))
    await waitFor(() => expect(deactivated).toBe(true))
    expect(await screen.findByRole('button', { name: /Reactivate/ })).toBeInTheDocument()
  })

  it("won't let you change your own role or deactivate yourself", async () => {
    server.use(
      http.get(apiUrl('/users/u1'), () =>
        HttpResponse.json(person({ id: 'u1', roleId: OWNER, roleName: 'Owner', restrictToAssignedWarehouses: false, warehouses: [], isYou: true })),
      ),
    )
    renderRoute(<UserDetailPage />, { path: '/settings/users/:id', url: '/settings/users/u1' })

    expect(await screen.findByRole('combobox', { name: 'Role' })).toBeDisabled()
    expect(screen.getByText(/can't change your own role/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Deactivate/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('group', { name: /Warehouses they can work in/ })).not.toBeInTheDocument()
  })
})

describe('Company settings', () => {
  const settings = (over: Partial<CompanySettingsDto> = {}): CompanySettingsDto => ({
    id: 't1',
    name: 'Acme Trading',
    slug: 'acme',
    currency: 'PHP',
    timeZone: 'Asia/Manila',
    allowNegativeStock: false,
    currencyLocked: true,
    createdAt: '2026-01-05T02:00:00Z',
    ...over,
  })

  beforeEach(() => as('company.manage'))

  it('locks the currency once stock has moved and saves the rest', async () => {
    let body: Record<string, unknown> | null = null
    server.use(
      http.get(apiUrl('/settings/company'), () => HttpResponse.json(settings())),
      http.put(apiUrl('/settings/company'), async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(settings({ name: 'Acme Trading Corp.', allowNegativeStock: true }))
      }),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(makeUser({ permissions: ['company.manage'] }))),
    )
    const user = userEvent.setup()
    renderRoute(<CompanySettingsPage />, { path: '/settings/company' })

    expect(await screen.findByRole('combobox', { name: 'Currency' })).toBeDisabled()
    expect(screen.getByText(/Locked: stock has already moved/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()

    const name = screen.getByLabelText('Company name')
    await user.clear(name)
    await user.type(name, 'Acme Trading Corp.')
    await user.click(screen.getByRole('checkbox', { name: /Allow negative stock/ }))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(body).toEqual({ name: 'Acme Trading Corp.', currency: 'PHP', timeZone: 'Asia/Manila', allowNegativeStock: true }))
  })
})

describe('Audit log', () => {
  const entry = (over: Partial<AuditLogDto>): AuditLogDto => ({
    id: 'a1',
    occurredAt: '2026-11-10T03:00:00Z',
    action: 'Updated',
    entityType: 'Tenant',
    entityId: 't1',
    userId: 'u1',
    userName: 'Maria Santos',
    ipAddress: '203.0.113.9',
    changes: [{ field: 'AllowNegativeStock', old: 'false', new: 'true' }],
    ...over,
  })

  it('reads as sentences and expands to show what changed', async () => {
    as('audit.view')
    server.use(
      http.get(apiUrl('/audit-logs/filters'), () => HttpResponse.json({ entityTypes: ['Tenant'], users: [{ id: 'u1', name: 'Maria Santos' }] })),
      http.get(apiUrl('/audit-logs'), () =>
        HttpResponse.json({
          items: [entry({}), entry({ id: 'a2', action: 'Auth.LoginFailed', entityType: 'User', changes: [], ipAddress: '198.51.100.4' })],
          page: 1,
          pageSize: 25,
          totalCount: 2,
        }),
      ),
    )
    const user = userEvent.setup()
    renderRoute(<AuditLogPage />, { path: '/settings/audit-log' })

    expect(await screen.findByText('updated company settings')).toBeInTheDocument()
    expect(screen.getByText('failed sign-in attempt')).toBeInTheDocument()

    await user.click(screen.getByText('updated company settings'))
    const table = await screen.findByRole('table', { name: 'Changed fields' })
    expect(within(table).getByRole('rowheader', { name: 'Allow negative stock' })).toBeInTheDocument()
    expect(within(table).getByText('true')).toBeInTheDocument()
    expect(screen.getByText(/From 203\.0\.113\.9/)).toBeInTheDocument()
  })
})

describe('Accepting an invitation', () => {
  it('shows the company and role, then signs the new user in', async () => {
    window.history.replaceState(null, '', '/accept-invitation#token=abc123')
    let accepted: Record<string, unknown> | null = null
    let authHeader: string | null = 'not called'
    // A previous user is still signed in on this browser.
    useAuthStore.setState({ accessToken: 'previous-users-token' })
    server.use(
      http.post(apiUrl('/auth/invitation'), () =>
        HttpResponse.json({ email: 'ivy@acme.test', firstName: 'Ivy', lastName: 'Reyes', companyName: 'Acme Trading', roleName: 'Inventory Staff' }),
      ),
      http.post(apiUrl('/auth/accept-invitation'), async ({ request }) => {
        accepted = (await request.json()) as Record<string, unknown>
        authHeader = request.headers.get('Authorization')
        return HttpResponse.json({ accessToken: 'new-token', expiresAt: '2030-01-01T00:00:00Z' })
      }),
      http.get(apiUrl('/auth/me'), () => HttpResponse.json(makeUser({ email: 'ivy@acme.test', firstName: 'Ivy' }))),
    )
    const user = userEvent.setup()
    renderRoute(<AcceptInvitationPage />, { path: '/accept-invitation' })

    expect(await screen.findByRole('heading', { name: 'Join Acme Trading' })).toBeInTheDocument()
    expect(screen.getByText('Inventory Staff')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Email' })).toHaveValue('ivy@acme.test')

    await user.type(screen.getByLabelText('Password'), 'Str0ngPass')
    await user.type(screen.getByLabelText('Confirm password'), 'Str0ngPass')
    await user.click(screen.getByRole('button', { name: 'Join and sign in' }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/')
    expect(accepted).toEqual({ token: 'abc123', password: 'Str0ngPass' })
    // Sent anonymously, so the audit log can't credit the previous user with this account's changes.
    expect(authHeader).toBeNull()
    expect(useAuthStore.getState().accessToken).toBe('new-token')
    expect(useAuthStore.getState().user?.email).toBe('ivy@acme.test')
  })

  it('explains a dead link instead of showing a form', async () => {
    window.history.replaceState(null, '', '/accept-invitation#token=used')
    server.use(http.post(apiUrl('/auth/invitation'), () => problem(422, 'TOKEN_INVALID', 'This invitation is invalid or has expired. Ask for a new one.')))
    renderRoute(<AcceptInvitationPage />, { path: '/accept-invitation' })

    expect(await screen.findByRole('heading', { name: "This invitation doesn't work" })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
  })
})
