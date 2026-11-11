import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { refreshAccessToken } from '@/lib/api/client'
import type { NotificationDto } from '@/lib/api/types'
import { entityPath } from '@/lib/links'
import { useAuthStore } from '@/stores/auth-store'
import { notificationKeys } from './api'
import { useLiveStatus } from './live-status'

const HUB_URL = '/api/v1/hubs/notifications'

/** Seconds until the JWT expires (no signature check needed: the server validates it, this only avoids sending a dead one). */
function secondsLeft(token: string) {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]!.replace(/-/g, '+').replace(/_/g, '/'))) as { exp?: number }
    return (payload.exp ?? 0) - Date.now() / 1000
  } catch {
    return 0
  }
}

/**
 * Every (re)connect asks for the token: a fresh one if the current one is about to expire. The server closes the
 * socket when the token it was opened with expires, and this is how the reconnect gets a valid one.
 */
async function currentToken() {
  const token = useAuthStore.getState().accessToken
  if (token && secondsLeft(token) > 30) return token
  return (await refreshAccessToken()) ?? ''
}

/** 0s, 2s, 5s, 10s, then every 30s, forever: the free-tier API sleeps and takes a while to wake up. */
const retry = { nextRetryDelayInMilliseconds: ({ previousRetryCount }: { previousRetryCount: number }) => [0, 2000, 5000, 10000][previousRetryCount] ?? 30000 }

/**
 * Keeps one live connection per signed-in company user (mounted once, in the app shell). A push refreshes the bell and
 * shows a toast; a reconnect refreshes too, in case something arrived while the connection was down.
 */
export function LiveNotifications() {
  const userId = useAuthStore((s) => (s.user?.tenant ? s.user.id : null))
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  useEffect(() => {
    if (!userId) return

    const refresh = () => queryClient.invalidateQueries({ queryKey: notificationKeys.all })
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: currentToken })
      .withAutomaticReconnect(retry)
      .configureLogging(LogLevel.None) // failures are expected (sleeping server, offline) and polling covers them
      .build()

    connection.on('notification', (n: NotificationDto) => {
      void refresh()
      const path = entityPath(n.entityType, n.entityId)
      toast(n.title, {
        description: n.message,
        action: path ? { label: 'View', onClick: () => navigate(path) } : undefined,
      })
    })
    connection.onreconnecting(() => useLiveStatus.setState({ connected: false }))
    connection.onreconnected(() => {
      useLiveStatus.setState({ connected: true })
      void refresh()
    })
    connection.onclose(() => useLiveStatus.setState({ connected: false }))

    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    // automaticReconnect only covers a connection that was once up; the FIRST start needs its own retry loop.
    const start = async (attempt = 0) => {
      try {
        await connection.start()
        if (!stopped) useLiveStatus.setState({ connected: true })
      } catch {
        if (!stopped && connection.state === HubConnectionState.Disconnected) {
          timer = setTimeout(() => void start(attempt + 1), retry.nextRetryDelayInMilliseconds({ previousRetryCount: attempt + 1 }))
        }
      }
    }
    void start()

    return () => {
      stopped = true
      clearTimeout(timer)
      useLiveStatus.setState({ connected: false })
      void connection.stop()
    }
  }, [userId, queryClient, navigate])

  return null
}
