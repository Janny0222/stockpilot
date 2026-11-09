import type { LucideIcon } from 'lucide-react'
import { LoaderCircle, TriangleExclamationPoint } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { useDelayedFlag } from '@/hooks/use-delayed-flag'
import { toApiError } from '@/lib/api/errors'
import { cn } from '@/lib/utils'
import { LogoMark } from './logo'

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-2xl font-bold tracking-tight text-foreground sm:text-[1.75rem]">{title}</h1>
        {description && <p className="mt-1 max-w-prose text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

/** Empty screens invite the next action instead of just saying "nothing here". */
export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon: LucideIcon
  title: string
  description?: ReactNode
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center px-6 py-14 text-center', className)}>
      <div className="mb-4 grid size-12 place-items-center rounded-xl bg-secondary text-secondary-foreground">
        <Icon className="size-6" aria-hidden="true" />
      </div>
      <h2 className="text-base font-semibold">{title}</h2>
      {description && <p className="mt-1 max-w-sm text-sm text-muted-foreground">{description}</p>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  )
}

/** Says what went wrong and offers the one useful next step: retry. Shows the trace ID for support. */
export function ErrorState({ error, onRetry, className }: { error: unknown; onRetry?: () => void; className?: string }) {
  const apiError = toApiError(error)
  return (
    <div role="alert" className={cn('flex flex-col items-center px-6 py-14 text-center', className)}>
      <div className="mb-4 grid size-12 place-items-center rounded-xl bg-destructive/10 text-destructive">
        <TriangleExclamationPoint className="size-6" aria-hidden="true" />
      </div>
      <h2 className="text-base font-semibold">Couldn't load this</h2>
      <p className="mt-1 max-w-sm text-sm text-muted-foreground">{apiError.message}</p>
      {apiError.traceId && <p className="mt-2 text-xs text-muted-foreground">Reference: {apiError.traceId}</p>}
      {onRetry && (
        <Button variant="outline" className="mt-5" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  )
}

export function FullPageLoader({ label = 'Loading StockPilot' }: { label?: string }) {
  const slow = useDelayedFlag(true, 4000)
  return (
    <div className="grid min-h-svh place-items-center bg-background px-6" role="status" aria-live="polite">
      <div className="flex flex-col items-center gap-4 text-center">
        <LogoMark className="size-10" />
        <LoaderCircle className="size-5 animate-spin text-muted-foreground" aria-hidden="true" />
        {slow ? <ServerWakingMessage /> : <span className="sr-only">{label}</span>}
      </div>
    </div>
  )
}

/**
 * The free hosting tier puts the API to sleep after 15 idle minutes; the first request then takes
 * up to a minute. Telling people that is better than an endless spinner.
 */
export function ServerWakingMessage({ className }: { className?: string }) {
  return (
    <div className={cn('max-w-xs', className)}>
      <p className="font-semibold">Waking up the server…</p>
      <p className="mt-1 text-sm text-muted-foreground">
        This demo runs on free hosting that sleeps when idle. The first load can take up to a minute.
      </p>
    </div>
  )
}
