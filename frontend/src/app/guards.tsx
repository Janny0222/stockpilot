import { ShieldOff } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, Navigate, Outlet, useLocation } from 'react-router'
import { EmptyState } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { useAuthStore, useHasPermission } from '@/stores/auth-store'

/**
 * Route guards decide what to RENDER. They are not security: a determined user can bypass any
 * client-side check, which is fine because the API independently rejects unauthorized calls.
 */
export function RequireAuth() {
  const status = useAuthStore((s) => s.status)
  const endedBy = useAuthStore((s) => s.endedBy)
  const location = useLocation()

  if (status === 'unauthenticated') {
    // Session expired or first visit: come back here after signing in. Deliberate sign-out: start fresh.
    const returnTo = location.pathname + location.search
    const keep = endedBy !== 'signout' && returnTo !== '/'
    return <Navigate to={`/login${keep ? `?returnTo=${encodeURIComponent(returnTo)}` : ''}`} replace />
  }
  return <Outlet />
}

/** Login/register pages: an already signed-in user goes straight to the app. */
export function RequireGuest() {
  const status = useAuthStore((s) => s.status)
  return status === 'authenticated' ? <Navigate to="/" replace /> : <Outlet />
}

export function RequirePermission({ permission, children }: { permission: string; children: ReactNode }) {
  const allowed = useHasPermission(permission)
  return allowed ? children : <Forbidden />
}

export function Forbidden() {
  return (
    <EmptyState
      icon={ShieldOff}
      title="You don't have access to this page"
      description="Your role doesn't include this area. Ask your company owner if you need access."
      action={
        <Button asChild variant="outline">
          <Link to="/">Go to dashboard</Link>
        </Button>
      }
    />
  )
}

/**
 * Only same-site paths, so "?returnTo=https://evil.example" can't send someone off-site after signing in. Resolved the
 * way the browser would, so tricks like "/\evil.example" or "/%09/evil.example" (which browsers turn into
 * "//evil.example") are caught too, not just the obvious "//".
 */
export function safeReturnTo(value: string | null) {
  if (!value || !value.startsWith('/')) return '/'
  try {
    const url = new URL(value, window.location.origin)
    return url.origin === window.location.origin ? url.pathname + url.search + url.hash : '/'
  } catch {
    return '/'
  }
}
