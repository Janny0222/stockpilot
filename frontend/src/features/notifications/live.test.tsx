import { act, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Toaster } from '@/components/ui/sonner'
import type { NotificationDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { renderRoute } from '@/test/render'
import { apiUrl, makeUser, server } from '@/test/server'
import { LiveNotifications } from './live'
import { useLiveStatus } from './live-status'
import { NotificationBell } from './notification-bell'

/** A stand-in for the SignalR connection: records handlers so the test can "push" like the server would. */
const fake = vi.hoisted(() => {
  const state = {
    handlers: new Map<string, (payload: unknown) => void>(),
    options: undefined as { accessTokenFactory: () => Promise<string> } | undefined,
    started: 0,
    stopped: 0,
  }
  class FakeConnection {
    state = 'Connected'
    on(name: string, fn: (payload: unknown) => void) {
      state.handlers.set(name, fn)
    }
    onreconnecting() {}
    onreconnected() {}
    onclose() {}
    async start() {
      state.started++
    }
    async stop() {
      state.stopped++
    }
  }
  class HubConnectionBuilder {
    withUrl(_url: string, options: { accessTokenFactory: () => Promise<string> }) {
      state.options = options
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return new FakeConnection()
    }
  }
  return { state, HubConnectionBuilder }
})

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: fake.HubConnectionBuilder,
  HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
  LogLevel: { None: 6 },
}))

/** An unsigned JWT-shaped token that expires in `seconds` (only "exp" is read on the client). */
const tokenExpiringIn = (seconds: number) => `x.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + seconds }))}.y`

const pushed: NotificationDto = {
  id: 'n9',
  type: 'PurchaseOrderAwaitingApproval',
  title: 'PO-000012 needs your approval',
  message: 'Philips Lighting, PHP 500.00, for Manila. Submitted by Jose Cruz.',
  entityType: 'PurchaseOrder',
  entityId: 'po1',
  createdAt: new Date().toISOString(),
  readAt: null,
}

beforeEach(() => {
  fake.state.handlers.clear()
  fake.state.started = 0
  fake.state.stopped = 0
  useLiveStatus.setState({ connected: false })
  useAuthStore.setState({ status: 'authenticated', accessToken: tokenExpiringIn(600), user: makeUser() })
})

const shell = (
  <>
    <LiveNotifications />
    <NotificationBell />
    <Toaster />
  </>
)

describe('Live notifications', () => {
  it('a push updates the bell, shows a toast, and its View button opens the record', async () => {
    let unread = 0
    server.use(http.get(apiUrl('/notifications/unread-count'), () => HttpResponse.json({ count: unread })))
    const user = userEvent.setup()
    renderRoute(shell)

    expect(await screen.findByRole('button', { name: 'Notifications' })).toBeInTheDocument()
    await waitFor(() => expect(useLiveStatus.getState().connected).toBe(true))

    unread = 1
    act(() => fake.state.handlers.get('notification')!(pushed))

    expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument()
    expect(await screen.findByText('PO-000012 needs your approval')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'View' }))
    expect(await screen.findByTestId('location')).toHaveTextContent('/purchases/po1')
  })

  it('refreshes an almost-expired token before (re)connecting', async () => {
    let refreshed = 0
    server.use(
      http.get(apiUrl('/notifications/unread-count'), () => HttpResponse.json({ count: 0 })),
      http.post(apiUrl('/auth/refresh'), () => {
        refreshed++
        return HttpResponse.json({ accessToken: tokenExpiringIn(900), expiresAt: '2030-01-01T00:00:00Z' })
      }),
    )
    renderRoute(shell)
    await waitFor(() => expect(fake.state.options).toBeDefined())

    // Still valid for 10 minutes → used as is.
    expect(await fake.state.options!.accessTokenFactory()).toBe(useAuthStore.getState().accessToken)
    expect(refreshed).toBe(0)

    // 10 seconds left → refreshed first, so the server doesn't accept and then immediately drop the connection.
    useAuthStore.setState({ accessToken: tokenExpiringIn(10) })
    const token = await fake.state.options!.accessTokenFactory()
    expect(refreshed).toBe(1)
    expect(token).toBe(useAuthStore.getState().accessToken)
  })

  it('does not connect for an account without a company, and disconnects on sign-out', async () => {
    useAuthStore.setState({ user: makeUser({ tenant: null, isPlatformAdmin: true }) })
    const { unmount } = renderRoute(<LiveNotifications />)
    await new Promise((r) => setTimeout(r, 50))
    expect(fake.state.started).toBe(0)
    unmount()

    useAuthStore.setState({ user: makeUser() })
    renderRoute(<LiveNotifications />)
    await waitFor(() => expect(fake.state.started).toBe(1))
    act(() => useAuthStore.getState().clear('signout'))
    await waitFor(() => expect(fake.state.stopped).toBe(1))
    expect(useLiveStatus.getState().connected).toBe(false)
  })
})
