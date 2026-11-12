import { BellOff, CheckCheck, Mail, Settings2 } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { Pagination } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { toApiError } from '@/lib/api/errors'
import { cn } from '@/lib/utils'
import { useMarkAllRead, useNotificationPreferences, useNotifications, useUnreadCount, useUpdateNotificationPreference } from './api'
import { NotificationRow } from './notification-parts'

export function NotificationsPage() {
  const [params, setParams] = useSearchParams()
  const unreadOnly = params.get('show') === 'unread'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const list = useNotifications({ page, pageSize: 20, unreadOnly })
  const unread = useUnreadCount()
  const markAll = useMarkAllRead()

  const show = (value: 'all' | 'unread') => setParams(value === 'unread' ? { show: 'unread' } : {}, { replace: true })

  return (
    <>
      <PageHeader
        title="Notifications"
        description="What needs your attention: stock running out, approvals and orders to ship."
        actions={
          <div className="flex flex-wrap gap-2">
            <Button asChild variant="outline">
              <Link to="/settings/notifications">
                <Settings2 /> Preferences
              </Link>
            </Button>
            <Button
              variant="outline"
              disabled={!unread.data || markAll.isPending}
              onClick={() =>
                markAll.mutate(undefined, {
                  onSuccess: (r) => toast.success(r.marked === 1 ? 'Marked 1 notification as read.' : `Marked ${r.marked} notifications as read.`),
                  onError: (e) => toast.error(toApiError(e).message),
                })
              }
            >
              <CheckCheck /> Mark all read
            </Button>
          </div>
        }
      />

      <div role="tablist" aria-label="Which notifications" className="mb-4 inline-flex rounded-lg border bg-muted p-1">
        {(
          [
            ['all', 'All'],
            ['unread', `Unread${unread.data ? ` (${unread.data})` : ''}`],
          ] as const
        ).map(([value, label]) => {
          const selected = (value === 'unread') === unreadOnly
          return (
            <button
              key={value}
              type="button"
              role="tab"
              aria-selected={selected}
              onClick={() => show(value)}
              className={cn(
                'rounded-md px-3.5 py-1.5 text-sm font-medium transition-colors',
                selected ? 'bg-card text-foreground shadow-card' : 'text-muted-foreground hover:text-foreground',
              )}
            >
              {label}
            </button>
          )
        })}
      </div>

      <Card className="max-w-3xl gap-0 overflow-hidden py-0 shadow-card">
        {list.isPending ? (
          <div className="grid gap-2 p-3">
            {Array.from({ length: 5 }, (_, i) => (
              <Skeleton key={i} className="h-20" />
            ))}
          </div>
        ) : list.isError ? (
          <ErrorState error={list.error} onRetry={() => list.refetch()} />
        ) : list.data.items.length === 0 ? (
          <EmptyState
            icon={BellOff}
            title={unreadOnly ? 'Nothing unread' : 'No notifications yet'}
            description={unreadOnly ? "You've seen everything." : 'Low stock, approvals and orders to ship will show up here as they happen.'}
          />
        ) : (
          <>
            <ul className="grid gap-0.5 p-1.5">
              {list.data.items.map((n) => (
                <li key={n.id}>
                  <NotificationRow notification={n} />
                </li>
              ))}
            </ul>
            <Pagination
              page={list.data.page}
              pageSize={list.data.pageSize}
              totalCount={list.data.totalCount}
              onPageChange={(p) =>
                setParams(
                  (prev) => {
                    const next = new URLSearchParams(prev)
                    if (p > 1) next.set('page', String(p))
                    else next.delete('page')
                    return next
                  },
                  { replace: true },
                )
              }
            />
          </>
        )}
      </Card>
    </>
  )
}

/** Settings → Notifications: one switch per type that concerns you. Saves on toggle. */
export function NotificationPreferencesPage() {
  const prefs = useNotificationPreferences()
  const update = useUpdateNotificationPreference()

  return (
    <>
      <PageHeader title="Notifications" description="Choose what reaches you, in the app and by email. This only affects you." />
      <Card className="max-w-3xl shadow-card">
        <CardHeader>
          <CardTitle>What you hear about</CardTitle>
          <CardDescription>You only see the kinds of notifications your role can act on.</CardDescription>
        </CardHeader>
        <CardContent>
          {prefs.isPending ? (
            <div className="grid gap-3">
              <Skeleton className="h-14" />
              <Skeleton className="h-14" />
            </div>
          ) : prefs.isError ? (
            <ErrorState error={prefs.error} onRetry={() => prefs.refetch()} className="py-6" />
          ) : prefs.data.length === 0 ? (
            <p className="text-sm text-muted-foreground">Your role doesn't receive any notifications yet.</p>
          ) : (
            <ul className="divide-y">
              {prefs.data.map((p) => {
                const id = `pref-${p.type}`
                const saving = update.isPending && update.variables?.type === p.type
                const save = (change: { inApp?: boolean; email?: boolean }, what: string, on: boolean) =>
                  update.mutate(
                    { type: p.type, ...change },
                    { onSuccess: () => toast.success(`${what}: ${on ? 'on' : 'off'}.`), onError: (e) => toast.error(toApiError(e).message) },
                  )
                return (
                  <li key={p.type} className="grid gap-3 py-4 first:pt-0 last:pb-0">
                    <div className="flex items-start justify-between gap-6">
                      <div>
                        <label htmlFor={id} className="font-medium">
                          {p.label}
                        </label>
                        <p id={`${id}-hint`} className="mt-0.5 text-sm text-muted-foreground">
                          {p.description}
                        </p>
                      </div>
                      <Switch id={id} describedBy={`${id}-hint`} checked={p.inApp} disabled={saving} onToggle={() => save({ inApp: !p.inApp }, p.label, !p.inApp)} />
                    </div>
                    {p.emailLabel && (
                      // Email is opt-in, so it sits under the in-app switch as its own, separate choice.
                      <div className="ml-4 flex items-start justify-between gap-6 border-l-2 pl-4">
                        <label htmlFor={`${id}-email`} className="flex items-center gap-2 text-sm">
                          <Mail className="size-4 text-muted-foreground" aria-hidden="true" />
                          {p.emailLabel}
                        </label>
                        <Switch
                          id={`${id}-email`}
                          checked={p.email}
                          disabled={saving}
                          onToggle={() => save({ email: !p.email }, p.emailLabel!, !p.email)}
                        />
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </CardContent>
      </Card>
    </>
  )
}

function Switch({ id, checked, disabled, onToggle, describedBy }: { id: string; checked: boolean; disabled?: boolean; onToggle: () => void; describedBy?: string }) {
  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-describedby={describedBy}
      disabled={disabled}
      onClick={onToggle}
      className={cn(
        'relative mt-0.5 inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-60',
        checked ? 'bg-primary' : 'bg-input',
      )}
    >
      <span aria-hidden="true" className={cn('inline-block size-5 rounded-full bg-white shadow transition-transform', checked ? 'translate-x-5.5' : 'translate-x-0.5')} />
    </button>
  )
}
