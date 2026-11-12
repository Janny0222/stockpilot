import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import type { NotificationDto, NotificationPreferenceDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, server } from '@/test/server'
import { NotificationBell } from './notification-bell'
import { NotificationPreferencesPage, NotificationsPage } from './notification-pages'

const note = (over: Partial<NotificationDto> = {}): NotificationDto => ({
  id: 'n1',
  type: 'LowStock',
  title: 'LED Bulb is running low',
  message: 'LED-9W: 13 available in Manila, at or below the reorder level of 15.',
  entityType: 'LowStock',
  entityId: 'w1',
  createdAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  readAt: null,
  ...over,
})

const page = (items: NotificationDto[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length })

beforeEach(() => useAuthStore.setState({ status: 'authenticated', user: makeUser({ permissions: ['purchases.manage'] }) }))

describe('Notification bell', () => {
  it('shows the unread count, and opening a notification marks it read and goes to what it is about', async () => {
    let marked: string | null = null
    server.use(
      http.get(apiUrl('/notifications/unread-count'), () => HttpResponse.json({ count: 12 })),
      http.get(apiUrl('/notifications'), () =>
        HttpResponse.json(
          page([
            note(),
            note({
              id: 'n2',
              type: 'PurchaseOrderAwaitingApproval',
              title: 'PO-000012 needs your approval',
              entityType: 'PurchaseOrder',
              entityId: 'po1',
              readAt: '2026-11-10T00:00:00Z',
            }),
          ]),
        ),
      ),
      http.post(apiUrl('/notifications/:id/read'), ({ params }) => {
        marked = params.id as string
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<NotificationBell />)

    const bell = await screen.findByRole('button', { name: 'Notifications, 12 unread' })
    expect(within(bell).getByText('9+')).toBeInTheDocument()

    await user.click(bell)
    const menu = await screen.findByRole('menu')
    expect(within(menu).getAllByText('5 minutes ago')).toHaveLength(2)
    // Unread state isn't colour-only: there's a text alternative.
    expect(within(menu).getAllByText('Unread')).toHaveLength(1)

    await user.click(within(menu).getByRole('menuitem', { name: /LED Bulb is running low/ }))
    expect(await screen.findByTestId('location')).toHaveTextContent('/inventory?warehouse=w1&low=1')
    expect(marked).toBe('n1')
  })

  it("doesn't mark an already-read notification again", async () => {
    let posts = 0
    server.use(
      http.get(apiUrl('/notifications/unread-count'), () => HttpResponse.json({ count: 0 })),
      http.get(apiUrl('/notifications'), () => HttpResponse.json(page([note({ readAt: '2026-11-10T00:00:00Z', entityType: 'SalesOrder', entityId: 'so1' })]))),
      http.post(apiUrl('/notifications/:id/read'), () => {
        posts++
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<NotificationBell />)

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))
    await user.click(await screen.findByRole('menuitem', { name: /LED Bulb/ }))
    expect(await screen.findByTestId('location')).toHaveTextContent('/sales/so1')
    expect(posts).toBe(0)
  })

  it('is hidden for an account without a company (platform admin)', async () => {
    useAuthStore.setState({ user: makeUser({ tenant: null, isPlatformAdmin: true }) })
    renderRoute(<NotificationBell />)
    await new Promise((r) => setTimeout(r, 50))
    expect(screen.queryByRole('button', { name: /Notifications/ })).not.toBeInTheDocument()
  })
})

describe('Notifications page', () => {
  it('filters to unread and marks everything read', async () => {
    const requested: string[] = []
    let markedAll = false
    server.use(
      http.get(apiUrl('/notifications/unread-count'), () => HttpResponse.json({ count: markedAll ? 0 : 1 })),
      http.get(apiUrl('/notifications'), ({ request }) => {
        requested.push(new URL(request.url).search)
        return HttpResponse.json(page(markedAll ? [] : [note()]))
      }),
      http.post(apiUrl('/notifications/read-all'), () => {
        markedAll = true
        return HttpResponse.json({ marked: 1 })
      }),
    )
    const user = userEvent.setup()
    renderRoute(<NotificationsPage />, { path: '/notifications' })

    expect(await screen.findByText('LED Bulb is running low')).toBeInTheDocument()
    await user.click(await screen.findByRole('tab', { name: 'Unread (1)' }))
    await waitFor(() => expect(requested.some((s) => s.includes('unreadOnly=true'))).toBe(true))

    await user.click(screen.getByRole('button', { name: /Mark all read/ }))
    expect(await screen.findByText('Nothing unread')).toBeInTheDocument()
  })
})

describe('Notification preferences', () => {
  it('turns a type off with a switch', async () => {
    let prefs: NotificationPreferenceDto[] = [
      {
        type: 'LowStock',
        label: 'Low and out-of-stock items',
        description: 'When an item drops to its reorder level.',
        inApp: true,
        email: false,
        emailLabel: 'Daily email with everything that needs reordering',
      },
    ]
    let body: unknown = null
    server.use(
      http.get(apiUrl('/notifications/preferences'), () => HttpResponse.json(prefs)),
      http.put(apiUrl('/notifications/preferences'), async ({ request }) => {
        body = await request.json()
        prefs = [{ ...prefs[0]!, inApp: false }]
        return HttpResponse.json(prefs)
      }),
    )
    const user = userEvent.setup()
    renderRoute(<NotificationPreferencesPage />, { path: '/settings/notifications' })

    const toggle = await screen.findByRole('switch', { name: 'Low and out-of-stock items' })
    expect(toggle).toBeChecked()
    await user.click(toggle)

    await waitFor(() => expect(toggle).not.toBeChecked())
    expect(body).toEqual({ preferences: [{ type: 'LowStock', inApp: false }] })
  })

  it('offers the daily email only where it exists, and sends just that switch', async () => {
    let body: unknown = null
    server.use(
      http.get(apiUrl('/notifications/preferences'), () =>
        HttpResponse.json([
          { type: 'LowStock', label: 'Low and out-of-stock items', description: 'd', inApp: true, email: false, emailLabel: 'Daily email' },
          { type: 'UserJoined', label: 'New team members', description: 'd', inApp: true, email: false, emailLabel: null },
        ]),
      ),
      http.put(apiUrl('/notifications/preferences'), async ({ request }) => {
        body = await request.json()
        return HttpResponse.json([
          { type: 'LowStock', label: 'Low and out-of-stock items', description: 'd', inApp: true, email: true, emailLabel: 'Daily email' },
          { type: 'UserJoined', label: 'New team members', description: 'd', inApp: true, email: false, emailLabel: null },
        ])
      }),
    )
    const user = userEvent.setup()
    renderRoute(<NotificationPreferencesPage />, { path: '/settings/notifications' })

    const email = await screen.findByRole('switch', { name: 'Daily email' })
    expect(email).not.toBeChecked()
    expect(screen.getAllByRole('switch')).toHaveLength(3) // two in-app switches, one email switch

    await user.click(email)
    await waitFor(() => expect(email).toBeChecked())
    expect(body).toEqual({ preferences: [{ type: 'LowStock', email: true }] })
  })
})
