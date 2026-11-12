import { CircleAlert, CircleCheck, LoaderCircle, Play } from 'lucide-react'
import { toast } from 'sonner'
import { ErrorState } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { toApiError } from '@/lib/api/errors'
import { formatDateTime, timeAgo } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useJobs, useRunJob } from './api'

const LABELS: Record<string, string> = {
  'token-cleanup': 'Token cleanup',
  'notification-retention': 'Notification retention',
  'low-stock-digest': 'Daily low-stock email',
}

/**
 * The in-process scheduler's status: when each job last ran, how it went, and when it runs next. "Next run" is "on
 * wake-up" when a run was missed while the server slept: it happens on the next start.
 */
export function JobsCard() {
  const jobs = useJobs()
  const run = useRunJob()
  const canRun = useHasPermission(P.Platform.TenantsManage)

  return (
    <Card className="mt-6 shadow-card">
      <CardHeader>
        <CardTitle>Background jobs</CardTitle>
        <CardDescription>
          They run inside the API, once a day. The free server sleeps when idle, so a missed run happens as soon as it wakes up.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {jobs.isPending ? (
          <div className="grid gap-3">
            <Skeleton className="h-14" />
            <Skeleton className="h-14" />
          </div>
        ) : jobs.isError ? (
          <ErrorState error={jobs.error} onRetry={() => jobs.refetch()} className="py-6" />
        ) : (
          <ul className="divide-y">
            {jobs.data.map((j) => {
              const running = run.isPending && run.variables === j.name
              const label = LABELS[j.name] ?? j.name
              return (
                <li key={j.name} className="flex flex-col gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-start sm:justify-between">
                  <div className="min-w-0">
                    <p className="font-medium">{label}</p>
                    <p className="text-sm text-muted-foreground">{j.description}</p>
                    <p className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                      <span>{j.schedule}</span>
                      {j.lastStartedAt ? (
                        <span className="inline-flex items-center gap-1" title={formatDateTime(j.lastStartedAt)}>
                          {j.lastSucceeded === false ? (
                            <CircleAlert className="size-3.5 text-destructive" aria-hidden="true" />
                          ) : (
                            <CircleCheck className="size-3.5 text-success" aria-hidden="true" />
                          )}
                          {j.lastSucceeded === false ? 'Failed' : 'Last run'} {timeAgo(j.lastStartedAt)}
                          {j.lastDurationMs != null && ` (${j.lastDurationMs} ms)`}
                        </span>
                      ) : (
                        <span>Never run yet</span>
                      )}
                      <span title={formatDateTime(j.nextRunAt)}>Next: {j.dueNow ? 'on the next wake-up' : formatDateTime(j.nextRunAt)}</span>
                    </p>
                    {j.lastMessage && <p className={j.lastSucceeded === false ? 'mt-1 text-sm text-destructive' : 'mt-1 text-sm'}>{j.lastMessage}</p>}
                  </div>
                  {canRun && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="shrink-0"
                      disabled={run.isPending}
                      onClick={() =>
                        run.mutate(j.name, {
                          onSuccess: (r) =>
                            r.ran ? (r.succeeded ? toast.success(`${label}: ${r.message}`) : toast.error(`${label}: ${r.message}`)) : toast.info(r.message),
                          onError: (e) => toast.error(toApiError(e).message),
                        })
                      }
                    >
                      {running ? <LoaderCircle className="animate-spin" aria-hidden="true" /> : <Play />}
                      Run now
                    </Button>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
