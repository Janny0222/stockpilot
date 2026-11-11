import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import type { TenantSummaryDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, server } from '@/test/server'
import { CompaniesPage, CompanyDetailPage } from './platform-pages'

const company = (over: Partial<TenantSummaryDto> = {}): TenantSummaryDto => ({
  id: 't1',
  name: 'Northwind Supply Co.',
  slug: 'northwind-supply',
  status: 'Active',
  currency: 'PHP',
  timeZone: 'Asia/Manila',
  createdAt: '2026-11-01T00:00:00Z',
  ownerName: 'Maria Santos',
  ownerEmail: 'owner@northwind.test',
  activeUsers: 4,
  warehouses: 3,
  products: 60,
  lastActivityAt: null,
  ...over,
})

const asAdmin = (...permissions: string[]) =>
  useAuthStore.setState({ status: 'authenticated', user: makeUser({ tenant: null, isPlatformAdmin: true, permissions }) })

describe('Companies list', () => {
  beforeEach(() => asAdmin('platform.tenants.view', 'platform.metrics.view'))

  it('shows platform totals and searches companies by owner email', async () => {
    const searches: (string | null)[] = []
    server.use(
      http.get(apiUrl('/platform/metrics'), () =>
        HttpResponse.json({ companies: 12, activeCompanies: 11, suspendedCompanies: 1, newCompaniesLast30Days: 3, users: 40, usersActiveLast7Days: 18 }),
      ),
      http.get(apiUrl('/platform/tenants'), ({ request }) => {
        searches.push(new URL(request.url).searchParams.get('search'))
        return HttpResponse.json({ items: [company()], page: 1, pageSize: 20, totalCount: 1 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<CompaniesPage />, { path: '/platform' })

    expect(await screen.findByText('3 new in the last 30 days')).toBeInTheDocument()
    expect(screen.getByText('1 suspended')).toBeInTheDocument()
    expect(await screen.findByText('Northwind Supply Co.')).toBeInTheDocument()
    expect(screen.getByText('3 · 60')).toBeInTheDocument()

    await user.type(screen.getByRole('searchbox', { name: 'Search companies' }), 'owner@northwind.test')
    await waitFor(() => expect(searches).toContain('owner@northwind.test'))
  })
})

describe('One company', () => {
  it('suspends only with a reason, then offers to reactivate', async () => {
    asAdmin('platform.tenants.view', 'platform.tenants.manage')
    let current = company()
    let body: unknown = null
    server.use(
      http.get(apiUrl('/platform/tenants/t1'), () => HttpResponse.json(current)),
      http.get(apiUrl('/platform/tenants'), () => HttpResponse.json({ items: [current], page: 1, pageSize: 20, totalCount: 1 })),
      http.post(apiUrl('/platform/tenants/t1/suspend'), async ({ request }) => {
        body = await request.json()
        current = company({ status: 'Suspended' })
        return HttpResponse.json(current)
      }),
    )
    const user = userEvent.setup()
    renderRoute(<CompanyDetailPage />, { path: '/platform/:id', url: '/platform/t1' })

    await user.click(await screen.findByRole('button', { name: /Suspend company/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: /Suspend company/ }))
    expect(await within(dialog).findByText('Say why, for the record.')).toBeInTheDocument()
    expect(body).toBeNull()

    await user.type(within(dialog).getByLabelText('Reason'), 'Payment overdue since October')
    await user.click(within(dialog).getByRole('button', { name: /Suspend company/ }))

    await waitFor(() => expect(body).toEqual({ reason: 'Payment overdue since October' }))
    expect(await screen.findByRole('button', { name: /Reactivate/ })).toBeInTheDocument()
    expect(screen.getByText(/Nobody from this company can sign in/)).toBeInTheDocument()
  })

  it('is read-only without the manage permission', async () => {
    asAdmin('platform.tenants.view')
    server.use(http.get(apiUrl('/platform/tenants/t1'), () => HttpResponse.json(company())))
    renderRoute(<CompanyDetailPage />, { path: '/platform/:id', url: '/platform/t1' })

    expect(await screen.findByText('owner@northwind.test')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Suspend/ })).not.toBeInTheDocument()
  })
})
