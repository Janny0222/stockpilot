import { ChevronRight, History } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { Pagination } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import type { AuditLogDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { useAuditFilters, useAuditLogs } from './api'

const ALL = 'all'

const ACTIONS = [
  { value: 'Created', label: 'Created' },
  { value: 'Updated', label: 'Updated' },
  { value: 'Deleted', label: 'Deleted' },
  { value: 'Auth', label: 'Sign-in and security' },
]

const EVENT_LABELS: Record<string, string> = {
  'Auth.LoginSucceeded': 'Signed in',
  'Auth.LoginFailed': 'Failed sign-in attempt',
  'Auth.RefreshTokenReuseDetected': 'Stolen session blocked',
  'Auth.PasswordReset': 'Reset their password',
  'Auth.LoggedOutEverywhere': 'Signed out everywhere',
  'Auth.InvitationAccepted': 'Accepted their invitation',
}

const ENTITY_LABELS: Record<string, string> = { Tenant: 'company settings', UserWarehouseAccess: 'warehouse access' }

/** "PurchaseOrderItem" → "purchase order item"; "IsActive" → "Is active". */
const words = (pascal: string) => pascal.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z])([A-Z][a-z])/g, '$1 $2')
const entityLabel = (type: string) => ENTITY_LABELS[type] ?? words(type).toLowerCase()
const fieldLabel = (field: string) => {
  const w = words(field).replace(/ Id$/, '')
  return w.charAt(0) + w.slice(1).toLowerCase()
}

const ENTITY_LINKS: Record<string, string> = {
  User: '/settings/users/',
  Product: '/products/',
  PurchaseOrder: '/purchases/',
  SalesOrder: '/sales/',
  Customer: '/customers/',
  Supplier: '/suppliers/',
  Warehouse: '/warehouses/',
  StockTransfer: '/inventory/transfers/',
}

function describe(entry: AuditLogDto) {
  if (entry.action.startsWith('Auth.')) return EVENT_LABELS[entry.action] ?? words(entry.action.slice(5))
  return `${entry.action} ${entityLabel(entry.entityType)}`
}

export function AuditLogPage() {
  const [params, setParams] = useSearchParams()
  const filters = useAuditFilters()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const value = (key: string) => params.get(key) ?? undefined
  const query = useAuditLogs({
    page,
    pageSize: 25,
    entityType: value('entityType'),
    action: value('action'),
    userId: value('userId'),
    from: value('from'),
    to: value('to'),
  })

  const update = (changes: Record<string, string | null | undefined>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(changes)) {
          if (v && v !== ALL) next.set(k, v)
          else next.delete(k)
        }
        next.delete('page')
        return next
      },
      { replace: true },
    )

  const filtered = ['entityType', 'action', 'userId', 'from', 'to'].some((k) => params.has(k))

  return (
    <>
      <PageHeader
        title="Audit log"
        description="Every change and sign-in in your company: who, what, when, and from where. Entries can't be edited or deleted."
      />
      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <div className="grid gap-3 border-b p-4 sm:grid-cols-2 lg:grid-cols-5">
          <FilterSelect
            label="Person"
            value={value('userId')}
            onChange={(v) => update({ userId: v })}
            options={(filters.data?.users ?? []).map((u) => ({ value: u.id, label: u.name }))}
            allLabel="Everyone"
          />
          <FilterSelect label="Action" value={value('action')} onChange={(v) => update({ action: v })} options={ACTIONS} allLabel="Any action" />
          <FilterSelect
            label="Record type"
            value={value('entityType')}
            onChange={(v) => update({ entityType: v })}
            options={(filters.data?.entityTypes ?? []).map((t) => ({ value: t, label: entityLabel(t).replace(/^./, (c) => c.toUpperCase()) }))}
            allLabel="All records"
          />
          <div className="grid gap-1.5">
            <Label htmlFor="audit-from">From</Label>
            <Input
              id="audit-from"
              type="date"
              className="h-10"
              value={value('from') ?? ''}
              max={value('to')}
              onChange={(e) => update({ from: e.target.value })}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="audit-to">To</Label>
            <Input id="audit-to" type="date" className="h-10" value={value('to') ?? ''} min={value('from')} onChange={(e) => update({ to: e.target.value })} />
          </div>
        </div>

        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : query.isPending ? (
          <div className="grid gap-2 p-4">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} className="h-14" />
            ))}
          </div>
        ) : query.data.items.length === 0 ? (
          <EmptyState
            icon={History}
            title={filtered ? 'Nothing matches these filters' : 'Nothing recorded yet'}
            description={filtered ? 'Try a wider date range or another person.' : 'Changes and sign-ins appear here as they happen.'}
          />
        ) : (
          <>
            <ul className="divide-y" aria-label="Audit entries, newest first">
              {query.data.items.map((entry) => (
                <AuditEntry key={entry.id} entry={entry} />
              ))}
            </ul>
            <Pagination
              page={query.data.page}
              pageSize={query.data.pageSize}
              totalCount={query.data.totalCount}
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

function FilterSelect({
  label,
  value,
  onChange,
  options,
  allLabel,
}: {
  label: string
  value: string | undefined
  onChange: (value: string) => void
  options: { value: string; label: string }[]
  allLabel: string
}) {
  const id = `audit-${label.toLowerCase().replace(/\s+/g, '-')}`
  return (
    <div className="grid gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Select value={value ?? ALL} onValueChange={onChange}>
        <SelectTrigger id={id} className="h-10 w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={ALL}>{allLabel}</SelectItem>
          {options.map((o) => (
            <SelectItem key={o.value} value={o.value}>
              {o.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}

function AuditEntry({ entry }: { entry: AuditLogDto }) {
  const link = entry.entityId && ENTITY_LINKS[entry.entityType] ? `${ENTITY_LINKS[entry.entityType]}${entry.entityId}` : null
  const failed = entry.action === 'Auth.LoginFailed' || entry.action === 'Auth.RefreshTokenReuseDetected'
  const summary = (
    <div className="flex min-w-0 flex-1 flex-col gap-0.5 sm:flex-row sm:items-baseline sm:gap-4">
      <time dateTime={entry.occurredAt} className="shrink-0 text-sm text-muted-foreground tabular sm:w-40">
        {formatDateTime(entry.occurredAt)}
      </time>
      <p className="min-w-0 text-sm">
        <span className="font-semibold">{entry.userName ?? 'System'}</span>{' '}
        <span className={failed ? 'font-medium text-destructive' : undefined}>{describe(entry).replace(/^./, (c) => c.toLowerCase())}</span>
        {entry.changes.length > 0 && (
          <span className="text-muted-foreground">
            {' '}
            · {entry.changes.length} {entry.changes.length === 1 ? 'field' : 'fields'}
          </span>
        )}
      </p>
    </div>
  )

  if (entry.changes.length === 0 && !entry.ipAddress) {
    return <li className="flex items-center gap-3 px-4 py-3 pl-11">{summary}</li>
  }

  return (
    <li>
      <details className="group">
        <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 hover:bg-accent/50 focus-visible:outline-2 focus-visible:outline-ring [&::-webkit-details-marker]:hidden">
          <ChevronRight className="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-90" aria-hidden="true" />
          {summary}
        </summary>
        <div className="grid gap-3 px-4 pb-4 pl-11 text-sm">
          {entry.changes.length > 0 && (
            <table className="w-full max-w-3xl text-left">
              <caption className="sr-only">Changed fields</caption>
              <thead className="text-xs text-muted-foreground">
                <tr>
                  <th scope="col" className="py-1 pr-4 font-medium">
                    Field
                  </th>
                  {entry.action !== 'Created' && (
                    <th scope="col" className="py-1 pr-4 font-medium">
                      Before
                    </th>
                  )}
                  {entry.action !== 'Deleted' && (
                    <th scope="col" className="py-1 font-medium">
                      After
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {entry.changes.map((c) => (
                  <tr key={c.field} className="border-t align-top">
                    <th scope="row" className="py-1.5 pr-4 font-medium">
                      {fieldLabel(c.field)}
                    </th>
                    {entry.action !== 'Created' && <td className="py-1.5 pr-4 break-all text-muted-foreground">{c.old ?? 'empty'}</td>}
                    {entry.action !== 'Deleted' && <td className="py-1.5 break-all">{c.new ?? 'empty'}</td>}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="text-xs text-muted-foreground">
            {entry.ipAddress && <>From {entry.ipAddress}. </>}
            {link && (
              <Link to={link} className="font-medium text-primary hover:underline">
                Open this {entityLabel(entry.entityType)}
              </Link>
            )}
          </p>
        </div>
      </details>
    </li>
  )
}
