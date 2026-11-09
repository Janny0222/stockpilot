import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { LogOut, Monitor, MonitorSmartphone, Smartphone } from 'lucide-react'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { authApi } from '@/features/auth/api'
import { useSignOut } from '@/features/auth/session'
import { toApiError } from '@/lib/api/errors'
import type { SessionDto } from '@/lib/api/types'
import { initials } from '@/lib/utils'
import { useAuthStore } from '@/stores/auth-store'

const sessionKeys = { all: ['sessions'] as const }

export function AccountPage() {
  const user = useAuthStore((s) => s.user)!
  const queryClient = useQueryClient()
  const signOut = useSignOut()

  const sessions = useQuery({ queryKey: sessionKeys.all, queryFn: authApi.sessions })

  const revoke = useMutation({
    mutationFn: (id: string) => authApi.revokeSession(id),
    onSuccess: () => {
      toast.success('Signed out that device.')
      return queryClient.invalidateQueries({ queryKey: sessionKeys.all })
    },
    onError: (e) => toast.error(toApiError(e).message),
  })

  return (
    <>
      <PageHeader title="Account & sessions" description="Your profile and the devices signed in to your account." />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Profile</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-5">
            <div className="flex items-center gap-4">
              <span className="grid size-14 place-items-center rounded-full bg-secondary text-lg font-bold text-secondary-foreground">
                {initials(user.firstName, user.lastName)}
              </span>
              <div className="min-w-0">
                <p className="truncate text-lg font-semibold">
                  {user.firstName} {user.lastName}
                </p>
                <p className="truncate text-muted-foreground">{user.email}</p>
              </div>
            </div>
            <dl className="grid gap-3 text-sm">
              <Row label="Role" value={user.role.name} />
              <Row label="Company" value={user.tenant?.name ?? 'Platform'} />
              {user.tenant && <Row label="Currency" value={user.tenant.currency} />}
              {user.warehouseScope.restricted && (
                <Row label="Warehouses" value={`${user.warehouseScope.warehouseIds.length} assigned`} />
              )}
            </dl>
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader className="flex flex-row items-start justify-between gap-4">
            <div>
              <CardTitle>Signed-in devices</CardTitle>
              <CardDescription className="mt-1">Sign out any device you don't recognize.</CardDescription>
            </div>
            <ConfirmDialog
              trigger={
                <Button variant="outline" size="sm" disabled={signOut.isPending}>
                  <LogOut /> Sign out everywhere
                </Button>
              }
              title="Sign out on every device?"
              description="You'll be signed out here too, and anyone using your account on another device loses access immediately."
              confirmLabel="Sign out everywhere"
              destructive
              onConfirm={() => signOut.mutate(true)}
            />
          </CardHeader>
          <CardContent>
            {sessions.isPending ? (
              <div className="grid gap-3">
                <Skeleton className="h-16" />
                <Skeleton className="h-16" />
              </div>
            ) : sessions.isError ? (
              <ErrorState error={sessions.error} onRetry={() => sessions.refetch()} className="py-6" />
            ) : (
              <ul className="divide-y">
                {sessions.data.map((s) => (
                  <SessionRow key={s.id} session={s} onRevoke={() => revoke.mutate(s.id)} revoking={revoke.isPending && revoke.variables === s.id} />
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
      </div>
    </>
  )
}

function SessionRow({ session, onRevoke, revoking }: { session: SessionDto; onRevoke: () => void; revoking: boolean }) {
  const device = describeDevice(session.userAgent)
  return (
    <li className="flex items-center gap-4 py-3.5">
      <span className="grid size-10 shrink-0 place-items-center rounded-lg bg-accent text-muted-foreground">
        <device.icon className="size-5" aria-hidden="true" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="flex items-center gap-2 font-medium">
          {device.label}
          {session.isCurrent && <Badge variant="secondary">This device</Badge>}
        </p>
        <p className="truncate text-sm text-muted-foreground">
          {session.ipAddress ?? 'Unknown IP'} · last active {formatRelative(session.lastActiveAt)}
        </p>
      </div>
      {!session.isCurrent && (
        <Button variant="ghost" size="sm" onClick={onRevoke} disabled={revoking}>
          Sign out
        </Button>
      )}
    </li>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[7rem_1fr] gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  )
}

function describeDevice(ua?: string | null) {
  const s = ua ?? ''
  const browser = /Edg\//.test(s) ? 'Edge' : /Chrome\//.test(s) ? 'Chrome' : /Firefox\//.test(s) ? 'Firefox' : /Safari\//.test(s) ? 'Safari' : null
  const os = /Windows/.test(s) ? 'Windows' : /Android/.test(s) ? 'Android' : /iPhone|iPad/.test(s) ? 'iOS' : /Mac OS X/.test(s) ? 'macOS' : /Linux/.test(s) ? 'Linux' : null
  const mobile = /Android|iPhone|iPad|Mobile/.test(s)
  return {
    icon: !s ? MonitorSmartphone : mobile ? Smartphone : Monitor,
    label: browser && os ? `${browser} on ${os}` : browser ?? os ?? 'Unknown device',
  }
}

const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
function formatRelative(iso: string) {
  const minutes = Math.round((new Date(iso).getTime() - Date.now()) / 60000)
  if (Math.abs(minutes) < 60) return rtf.format(minutes, 'minute')
  const hours = Math.round(minutes / 60)
  if (Math.abs(hours) < 24) return rtf.format(hours, 'hour')
  return rtf.format(Math.round(hours / 24), 'day')
}
