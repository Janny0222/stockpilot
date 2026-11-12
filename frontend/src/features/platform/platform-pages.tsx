import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Ban, Building2, CircleCheck, LoaderCircle, Search, UsersRound, type LucideIcon } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { FormAlert } from '@/features/auth/components/form-alert'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { toApiError } from '@/lib/api/errors'
import type { TenantStatus, TenantSummaryDto } from '@/lib/api/types'
import { formatDay, timeAgo } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { usePlatformMetrics, useSetTenantStatus, useTenant, useTenants } from './api'
import { JobsCard } from './jobs-card'

const count = new Intl.NumberFormat('en-PH')

function StatusBadge({ status }: { status: TenantStatus }) {
  return status === 'Active' ? (
    <Badge variant="outline" className="border-success/30 text-success">
      Active
    </Badge>
  ) : (
    <Badge variant="outline" className="border-destructive/30 text-destructive">
      Suspended
    </Badge>
  )
}

function Tile({ icon: Icon, label, value, sub }: { icon: LucideIcon; label: string; value: number | undefined; sub?: string }) {
  return (
    <Card className="gap-0 py-0 shadow-card">
      <CardContent className="grid gap-1 p-5">
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Icon className="size-4 text-primary" aria-hidden="true" />
          {label}
        </p>
        {value === undefined ? <Skeleton className="h-8 w-16" /> : <p className="text-2xl font-bold tracking-tight tabular">{count.format(value)}</p>}
        {sub && <p className="text-xs text-muted-foreground">{sub}</p>}
      </CardContent>
    </Card>
  )
}

export function CompaniesPage() {
  const navigate = useNavigate()
  const metrics = usePlatformMetrics()
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const status = (params.get('status') as TenantStatus | null) ?? undefined
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debounced = useDebouncedValue(search)
  const query = useTenants({ page, pageSize: 20, search: debounced || undefined, status, sort: sortParam ?? undefined })

  const update = (changes: Record<string, string | null>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(changes)) {
          if (v) next.set(k, v)
          else next.delete(k)
        }
        return next
      },
      { replace: true },
    )

  const m = metrics.data
  const columns: Column<TenantSummaryDto>[] = [
    {
      key: 'name',
      header: 'Company',
      sortField: 'name',
      cell: (t) => (
        <div className="min-w-0">
          <p className="truncate font-semibold">{t.name}</p>
          <p className="truncate text-xs text-muted-foreground">{t.ownerEmail ?? t.slug}</p>
        </div>
      ),
    },
    { key: 'status', header: 'Status', cell: (t) => <StatusBadge status={t.status} /> },
    { key: 'users', header: 'Users', sortField: 'activeUsers', className: 'tabular', cell: (t) => t.activeUsers },
    {
      key: 'size',
      header: 'Warehouses · products',
      hideOnMobile: true,
      className: 'tabular text-muted-foreground',
      cell: (t) => `${t.warehouses} · ${t.products}`,
    },
    {
      key: 'activity',
      header: 'Last sign-in',
      sortField: 'lastActivityAt',
      hideOnMobile: true,
      cell: (t) => <span className="text-muted-foreground">{t.lastActivityAt ? timeAgo(t.lastActivityAt) : 'Never'}</span>,
    },
    { key: 'created', header: 'Registered', sortField: 'createdAt', hideOnMobile: true, cell: (t) => formatDay(t.createdAt) },
  ]

  return (
    <>
      <PageHeader title="Companies" description="Every company on StockPilot, as an account. Their business data stays private to them." />

      <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Tile icon={Building2} label="Companies" value={m?.companies} sub={m && `${m.newCompaniesLast30Days} new in the last 30 days`} />
        <Tile
          icon={CircleCheck}
          label="Active"
          value={m?.activeCompanies}
          sub={m && (m.suspendedCompanies ? `${m.suspendedCompanies} suspended` : 'None suspended')}
        />
        <Tile icon={UsersRound} label="Users" value={m?.users} sub="Active accounts across all companies" />
        <Tile icon={UsersRound} label="Signed in this week" value={m?.usersActiveLast7Days} sub="Users active in the last 7 days" />
      </div>

      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <div className="flex flex-col gap-3 border-b p-4 sm:flex-row">
          <div className="relative max-w-sm flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              type="search"
              value={search}
              onChange={(e) => update({ q: e.target.value || null, page: null })}
              placeholder="Company name, ID or owner email"
              aria-label="Search companies"
              className="h-10 pl-9"
            />
          </div>
          <Select value={status ?? 'all'} onValueChange={(v) => update({ status: v === 'all' ? null : v, page: null })}>
            <SelectTrigger className="h-10 w-full sm:w-44" aria-label="Filter by status">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="Active">Active</SelectItem>
              <SelectItem value="Suspended">Suspended</SelectItem>
            </SelectContent>
          </Select>
        </div>
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Companies"
              columns={columns}
              rows={query.data?.items}
              rowKey={(t) => t.id}
              isLoading={query.isPending}
              sort={sort}
              onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
              onRowClick={(t) => navigate(`/platform/${t.id}`)}
              empty={
                <EmptyState
                  icon={Building2}
                  title={debounced || status ? 'No companies match these filters' : 'No companies yet'}
                  description="Companies appear here as soon as they register."
                />
              }
            />
            {query.data && query.data.totalCount > 0 && (
              <Pagination
                page={query.data.page}
                pageSize={query.data.pageSize}
                totalCount={query.data.totalCount}
                onPageChange={(p) => update({ page: p > 1 ? String(p) : null })}
              />
            )}
          </>
        )}
      </Card>

      <JobsCard />
    </>
  )
}

export function CompanyDetailPage() {
  const { id = '' } = useParams()
  const query = useTenant(id)
  const canManage = useHasPermission(P.Platform.TenantsManage)
  const [suspending, setSuspending] = useState(false)
  const setStatus = useSetTenantStatus(id)

  if (query.isPending) return <Skeleton className="h-64 max-w-3xl" />
  if (query.isError) return <ErrorState error={query.error} onRetry={() => query.refetch()} />
  const t = query.data

  return (
    <>
      <Button asChild variant="ghost" size="sm" className="mb-3 -ml-2 text-muted-foreground">
        <Link to="/platform">
          <ArrowLeft /> All companies
        </Link>
      </Button>
      <PageHeader
        title={t.name}
        description={
          <span className="inline-flex flex-wrap items-center gap-2">
            {t.slug} <StatusBadge status={t.status} />
          </span>
        }
        actions={
          canManage &&
          (t.status === 'Active' ? (
            <Button variant="outline" className="text-destructive hover:text-destructive" onClick={() => setSuspending(true)}>
              <Ban /> Suspend company
            </Button>
          ) : (
            <Button
              variant="outline"
              disabled={setStatus.isPending}
              onClick={() =>
                setStatus.mutate(
                  { suspend: false },
                  { onSuccess: () => toast.success(`${t.name} is active again.`), onError: (e) => toast.error(toApiError(e).message) },
                )
              }
            >
              <CircleCheck /> Reactivate
            </Button>
          ))
        }
      />

      {t.status === 'Suspended' && (
        <div className="mb-6 max-w-3xl">
          <FormAlert tone="error">Nobody from this company can sign in. Their data is untouched and comes back as-is when you reactivate.</FormAlert>
        </div>
      )}

      <div className="grid max-w-3xl gap-6 md:grid-cols-2">
        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Account</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-3 text-sm">
              <Row label="Owner" value={t.ownerName ?? '—'} />
              <Row label="Owner email" value={t.ownerEmail ?? '—'} />
              <Row label="Registered" value={formatDay(t.createdAt)} />
              <Row label="Last sign-in" value={t.lastActivityAt ? timeAgo(t.lastActivityAt) : 'Never'} />
              <Row label="Currency" value={t.currency} />
              <Row label="Time zone" value={t.timeZone} />
            </dl>
          </CardContent>
        </Card>
        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Size</CardTitle>
            <CardDescription>Counts only. What's inside stays private to the company.</CardDescription>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-3 text-sm">
              <Row label="Active users" value={String(t.activeUsers)} />
              <Row label="Warehouses" value={String(t.warehouses)} />
              <Row label="Products" value={String(t.products)} />
            </dl>
          </CardContent>
        </Card>
      </div>

      <Dialog open={suspending} onOpenChange={setSuspending}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Suspend {t.name}?</DialogTitle>
            <DialogDescription>
              Everyone in this company is signed out within a minute and can't sign in until you reactivate it. Nothing is deleted.
            </DialogDescription>
          </DialogHeader>
          {suspending && <SuspendForm id={id} name={t.name} onDone={() => setSuspending(false)} />}
        </DialogContent>
      </Dialog>
    </>
  )
}

const suspendSchema = z.object({ reason: z.string().trim().min(1, 'Say why, for the record.').max(500) })

function SuspendForm({ id, name, onDone }: { id: string; name: string; onDone: () => void }) {
  const setStatus = useSetTenantStatus(id)
  const form = useForm<z.infer<typeof suspendSchema>>({ resolver: zodResolver(suspendSchema), defaultValues: { reason: '' } })

  const onSubmit = form.handleSubmit(async ({ reason }) => {
    try {
      await setStatus.mutateAsync({ suspend: true, reason })
      toast.success(`${name} is suspended.`)
      onDone()
    } catch (e) {
      form.setError('root', { message: toApiError(e).message })
    }
  })

  const { errors } = form.formState
  return (
    <>
      <form id="suspend-form" noValidate onSubmit={onSubmit} className="grid gap-2">
        {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}
        <Label htmlFor="suspend-reason">Reason</Label>
        <Textarea
          id="suspend-reason"
          rows={3}
          placeholder="e.g. Payment overdue since October"
          aria-invalid={errors.reason ? true : undefined}
          aria-describedby="suspend-reason-hint"
          {...form.register('reason')}
        />
        {errors.reason?.message ? (
          <p role="alert" className="text-sm text-destructive">
            {errors.reason.message}
          </p>
        ) : (
          <p id="suspend-reason-hint" className="text-sm text-muted-foreground">
            The company sees this in its audit log.
          </p>
        )}
      </form>
      <DialogFooter>
        <Button variant="outline" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" form="suspend-form" variant="destructive" disabled={setStatus.isPending}>
          {setStatus.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Suspend company
        </Button>
      </DialogFooter>
    </>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="truncate text-right font-medium">{value}</dd>
    </div>
  )
}
