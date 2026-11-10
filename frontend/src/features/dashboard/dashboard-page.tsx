import { ArrowDownRight, ArrowUpRight, Boxes, ChartColumn, PackageX, Receipt, ShoppingCart, TrendingUp, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { MovementTypeBadge, QuantityChange, StockStatusBadge, WarehouseSelect } from '@/features/inventory/components/inventory-parts'
import { useMovements, useStock } from '@/features/inventory/api'
import type { DashboardDto } from '@/lib/api/types'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useAuthStore, useHasPermission } from '@/stores/auth-store'
import { useDashboard, type DashboardParams } from './api'
import { BarList, ColumnChart, compactMoney, SERIES } from './charts'

function greeting() {
  const h = new Date().getHours()
  return h < 12 ? 'Good morning' : h < 18 ? 'Good afternoon' : 'Good evening'
}

const PERIODS = [7, 30, 90] as const
const range = new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric' })
const fmtRange = (from: string, to: string) => `${range.format(new Date(`${from}T00:00:00`))} – ${range.format(new Date(`${to}T00:00:00`))}`
const time = new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const dayLabel = new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric' })
const dayFull = new Intl.DateTimeFormat('en-PH', { weekday: 'short', month: 'short', day: 'numeric' })

/** Every number on this page comes from /dashboard or the inventory API: nothing is hard-coded. */
export function DashboardPage() {
  const user = useAuthStore((s) => s.user)!
  const [params, setParams] = useSearchParams()
  const days = (PERIODS.find((p) => String(p) === params.get('days')) ?? 30) as DashboardParams['days']
  const warehouseId = params.get('warehouse') ?? undefined
  const query = useDashboard({ days, warehouseId })
  const canSeeInventory = useHasPermission(P.Inventory.View)

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

  const d = query.data
  return (
    <>
      <PageHeader
        title={`${greeting()}, ${user.firstName}`}
        description={d ? `${user.tenant?.name ?? 'Your company'} · last ${d.days} days (${fmtRange(d.from, d.to)})` : 'Loading your numbers…'}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {canSeeInventory && (
              <WarehouseSelect id="dash-warehouse" value={warehouseId} onChange={(v) => update({ warehouse: v ?? null })} allowAll className="w-48" />
            )}
            <div role="group" aria-label="Period" className="inline-flex rounded-lg border bg-card p-0.5">
              {PERIODS.map((p) => (
                <button
                  key={p}
                  type="button"
                  aria-pressed={days === p}
                  onClick={() => update({ days: p === 30 ? null : String(p) })}
                  className={cn(
                    'rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring',
                    days === p ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground',
                  )}
                >
                  {p} days
                </button>
              ))}
            </div>
          </div>
        }
      />

      {query.isError ? (
        <Card className="shadow-card">
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        </Card>
      ) : !d ? (
        <DashboardSkeleton />
      ) : !d.stock && !d.sales && !d.purchasing ? (
        <Card className="p-6 shadow-card">
          <p className="text-muted-foreground">Your role doesn't include any dashboard figures. Use the menu to get to your work.</p>
        </Card>
      ) : (
        <div className={cn('grid gap-6 transition-opacity', query.isFetching && 'opacity-70')}>
          <KpiRow d={d} warehouseId={warehouseId} />
          {d.sales && d.salesTrend && (
            <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
              <SalesTrendCard d={d} />
              <TopProductsCard d={d} />
            </div>
          )}
          {d.stock && (
            <div className="grid items-start gap-6 lg:grid-cols-2 xl:grid-cols-3">
              <StockByWarehouseCard d={d} />
              <LowStockCard warehouseId={warehouseId} />
              <RecentMovementsCard warehouseId={warehouseId} />
            </div>
          )}
        </div>
      )}
    </>
  )
}

// ---------------- KPI tiles ----------------

function KpiRow({ d, warehouseId }: { d: DashboardDto; warehouseId?: string }) {
  const currency = useCurrency()
  const tiles: ReactNode[] = []
  const lowHref = `/inventory?low=1${warehouseId ? `&warehouse=${warehouseId}` : ''}`

  if (d.sales) {
    tiles.push(
      <Kpi
        key="revenue"
        icon={TrendingUp}
        label="Revenue"
        value={formatMoney(d.sales.revenue, currency)}
        delta={change(d.sales.revenue, d.sales.previousRevenue, d.days)}
        sub={`${d.sales.ordersShipped} order${d.sales.ordersShipped === 1 ? '' : 's'} shipped`}
      />,
    )
    if (d.sales.grossProfit !== null) {
      tiles.push(
        <Kpi
          key="profit"
          icon={ChartColumn}
          label="Gross profit"
          value={formatMoney(d.sales.grossProfit, currency)}
          delta={change(d.sales.grossProfit, d.sales.previousGrossProfit ?? 0, d.days)}
          sub={d.sales.marginPercent !== null ? `${d.sales.marginPercent}% margin` : 'No sales yet'}
        />,
      )
    }
  }

  if (d.stock) {
    tiles.push(
      d.stock.value !== null ? (
        <Kpi
          key="stock"
          icon={Boxes}
          label="Stock value"
          value={formatMoney(d.stock.value, currency)}
          sub={`${formatQuantity(d.stock.units)} units · ${d.stock.itemsInStock} items`}
          href="/inventory"
        />
      ) : (
        <Kpi
          key="stock"
          icon={Boxes}
          label="Units on hand"
          value={formatQuantity(d.stock.units)}
          sub={`${d.stock.itemsInStock} items in stock`}
          href="/inventory"
        />
      ),
      <Kpi
        key="reorder"
        icon={PackageX}
        label="Needs reordering"
        value={String(d.stock.lowStock + d.stock.outOfStock)}
        sub={d.stock.outOfStock > 0 ? `${d.stock.outOfStock} out of stock` : 'None out of stock'}
        attention={d.stock.outOfStock > 0}
        href={lowHref}
      />,
    )
  }

  if (d.sales) {
    tiles.push(
      <Kpi
        key="open-sales"
        icon={Receipt}
        label="Orders to ship"
        value={String(d.sales.openOrders)}
        sub={d.stock ? `${formatQuantity(d.stock.reservedUnits)} units reserved` : 'Confirmed, not yet shipped'}
        href="/sales?status=Confirmed"
      />,
    )
  }

  if (d.purchasing) {
    const p = d.purchasing
    tiles.push(
      <Kpi
        key="incoming"
        icon={ShoppingCart}
        label="Open purchase orders"
        value={String(p.openOrders)}
        sub={
          [p.awaitingApproval > 0 && `${p.awaitingApproval} awaiting approval`, p.overdue > 0 && `${p.overdue} overdue`].filter(Boolean).join(' · ') ||
          `${formatQuantity(p.incomingUnits)} units incoming`
        }
        attention={p.overdue > 0}
        href="/purchases"
      />,
    )
  }

  return <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-6">{tiles}</div>
}

/** Signed change vs the previous period of the same length. No % when there was nothing before (it'd be ∞). */
function change(current: number, previous: number, days: number) {
  if (previous === 0) return current === 0 ? undefined : { text: `vs nothing in the previous ${days} days`, up: true, neutral: true }
  const pct = ((current - previous) / Math.abs(previous)) * 100
  return { text: `${pct >= 0 ? '+' : ''}${pct.toFixed(1)}% vs previous ${days} days`, up: pct >= 0, neutral: false }
}

function Kpi({
  icon: Icon,
  label,
  value,
  sub,
  delta,
  href,
  attention = false,
}: {
  icon: LucideIcon
  label: string
  value: string
  sub?: string
  delta?: { text: string; up: boolean; neutral: boolean }
  href?: string
  attention?: boolean
}) {
  const body = (
    <Card className="h-full gap-0 py-0 shadow-card transition-colors">
      <CardContent className="flex h-full flex-col gap-1 p-5">
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Icon className={cn('size-4', attention ? 'text-destructive' : 'text-primary')} aria-hidden="true" />
          {label}
        </p>
        <p className="truncate text-2xl font-bold tracking-tight">{value}</p>
        {delta && (
          <p
            className={cn(
              'flex items-center gap-1 text-xs font-medium',
              delta.neutral ? 'text-muted-foreground' : delta.up ? 'text-success' : 'text-destructive',
            )}
          >
            {!delta.neutral &&
              (delta.up ? <ArrowUpRight className="size-3.5" aria-hidden="true" /> : <ArrowDownRight className="size-3.5" aria-hidden="true" />)}
            {delta.text}
          </p>
        )}
        {sub && <p className={cn('mt-auto pt-1 text-xs', attention ? 'font-medium text-destructive' : 'text-muted-foreground')}>{sub}</p>}
      </CardContent>
    </Card>
  )
  return href ? (
    <Link to={href} className="rounded-xl focus-visible:outline-2 focus-visible:outline-ring [&>div]:hover:border-primary/40">
      {body}
    </Link>
  ) : (
    body
  )
}

// ---------------- Charts ----------------

function SalesTrendCard({ d }: { d: DashboardDto }) {
  const currency = useCurrency()
  const points = d.salesTrend ?? []
  // 90 daily columns would be hairlines: group into weeks (7-day buckets ending today) for the long view.
  const weekly = d.days > 30
  const buckets = weekly
    ? Array.from({ length: Math.ceil(points.length / 7) }, (_, b) => points.slice(Math.max(0, points.length - (b + 1) * 7), points.length - b * 7)).reverse()
    : points.map((p) => [p])
  const categories = buckets.map((b) => {
    const first = dayLabel.format(new Date(`${b[0]!.date}T00:00:00`))
    const last = dayFull.format(new Date(`${b[b.length - 1]!.date}T00:00:00`))
    return { key: b[0]!.date, axisLabel: first, title: weekly ? `${first} – ${last}` : dayFull.format(new Date(`${b[0]!.date}T00:00:00`)) }
  })
  const sum = (b: typeof points, pick: (p: (typeof points)[number]) => number) => Math.round(b.reduce((s, p) => s + pick(p), 0) * 100) / 100
  const series = [
    { key: 'revenue', label: 'Revenue', color: SERIES.primary, values: buckets.map((b) => sum(b, (p) => p.revenue)) },
    ...(d.canViewCost
      ? [{ key: 'profit', label: 'Gross profit', color: SERIES.secondary, values: buckets.map((b) => sum(b, (p) => p.grossProfit ?? 0)) }]
      : []),
  ]
  const nothing = points.every((p) => p.revenue === 0)
  const per = weekly ? 'week' : 'day'
  return (
    <Card className="shadow-card">
      <CardHeader>
        <CardTitle>Sales per {per}</CardTitle>
        <CardDescription>
          Value of goods shipped each {per}
          {d.canViewCost ? ', and the profit on them' : ''}.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {nothing ? (
          <p className="py-16 text-center text-sm text-muted-foreground">No goods shipped in the last {d.days} days.</p>
        ) : (
          <ColumnChart
            categories={categories}
            series={series}
            format={(v) => formatMoney(v, currency)}
            formatAxis={(v) => (Math.abs(v) >= 10_000 ? compactMoney(v, currency) : formatMoney(v, currency).replace(/\.00$/, ''))}
            label={`Sales per ${per}, last ${d.days} days`}
          />
        )}
      </CardContent>
    </Card>
  )
}

function TopProductsCard({ d }: { d: DashboardDto }) {
  const currency = useCurrency()
  return (
    <Card className="shadow-card">
      <CardHeader>
        <CardTitle>Best sellers</CardTitle>
        <CardDescription>By revenue, last {d.days} days.</CardDescription>
      </CardHeader>
      <CardContent>
        <BarList
          items={(d.topProducts ?? []).map((p) => ({ key: p.productId, label: p.productName, sub: `${formatQuantity(p.quantity)} sold`, value: p.revenue }))}
          format={(v) => formatMoney(v, currency)}
          empty="Nothing sold in this period."
        />
      </CardContent>
    </Card>
  )
}

function StockByWarehouseCard({ d }: { d: DashboardDto }) {
  const currency = useCurrency()
  const byValue = d.canViewCost
  return (
    <Card className="shadow-card">
      <CardHeader>
        <CardTitle>Stock by warehouse</CardTitle>
        <CardDescription>{byValue ? 'Value on hand at average cost.' : 'Units on hand.'}</CardDescription>
      </CardHeader>
      <CardContent>
        <BarList
          items={(d.stockByWarehouse ?? []).map((w) => ({
            key: w.warehouseId,
            label: w.name,
            sub: w.lowStock + w.outOfStock > 0 ? `${w.lowStock + w.outOfStock} to reorder` : undefined,
            value: byValue ? (w.value ?? 0) : w.units,
          }))}
          format={(v) => (byValue ? formatMoney(v, currency) : `${formatQuantity(v)} units`)}
          empty="No stock recorded yet."
        />
      </CardContent>
    </Card>
  )
}

// ---------------- Lists ----------------

function LowStockCard({ warehouseId }: { warehouseId?: string }) {
  const query = useStock({ page: 1, pageSize: 5, lowStockOnly: true, warehouseId, sort: 'available:asc' })
  return (
    <Card className="shadow-card">
      <CardHeader className="flex flex-row items-start justify-between gap-3">
        <div>
          <CardTitle>Running low</CardTitle>
          <CardDescription className="mt-1">At or below the reorder level.</CardDescription>
        </div>
        <Link to={`/inventory?low=1${warehouseId ? `&warehouse=${warehouseId}` : ''}`} className="text-sm font-medium text-primary hover:underline">
          View all
        </Link>
      </CardHeader>
      <CardContent>
        {query.isPending ? (
          <Skeleton className="h-40" />
        ) : query.isError ? (
          <ErrorState error={query.error} />
        ) : query.data.items.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">Everything is above its reorder level.</p>
        ) : (
          <ul className="divide-y">
            {query.data.items.map((s) => (
              <li key={`${s.warehouseId}-${s.variantId}`} className="flex items-center justify-between gap-3 py-2.5 text-sm first:pt-0 last:pb-0">
                <div className="min-w-0">
                  <Link to={`/products/${s.productId}`} className="block truncate font-medium hover:underline">
                    {s.productName}
                  </Link>
                  <p className="text-xs text-muted-foreground">
                    {s.sku} · {s.warehouseCode} · {formatQuantity(s.available)} of {formatQuantity(s.reorderLevel)} {s.unitOfMeasure}
                  </p>
                </div>
                <StockStatusBadge status={s.status} />
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function RecentMovementsCard({ warehouseId }: { warehouseId?: string }) {
  const query = useMovements({ page: 1, pageSize: 6, warehouseId })
  return (
    <Card className="shadow-card lg:col-span-2 xl:col-span-1">
      <CardHeader className="flex flex-row items-start justify-between gap-3">
        <div>
          <CardTitle>Latest stock movements</CardTitle>
          <CardDescription className="mt-1">Straight from the ledger.</CardDescription>
        </div>
        <Link to="/inventory?tab=movements" className="text-sm font-medium text-primary hover:underline">
          View all
        </Link>
      </CardHeader>
      <CardContent>
        {query.isPending ? (
          <Skeleton className="h-40" />
        ) : query.isError ? (
          <ErrorState error={query.error} />
        ) : query.data.items.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">No stock has moved yet.</p>
        ) : (
          <ul className="divide-y">
            {query.data.items.map((m) => (
              <li key={m.id} className="flex items-center justify-between gap-3 py-2.5 text-sm first:pt-0 last:pb-0">
                <div className="min-w-0">
                  <p className="truncate font-medium">{m.productName}</p>
                  <p className="flex items-center gap-2 text-xs text-muted-foreground">
                    <MovementTypeBadge type={m.type} /> {time.format(new Date(m.occurredAt))}
                  </p>
                </div>
                <QuantityChange value={m.quantityChange} />
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function DashboardSkeleton() {
  return (
    <div className="grid gap-6">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-6">
        {Array.from({ length: 6 }, (_, i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
      <Skeleton className="h-80" />
    </div>
  )
}
