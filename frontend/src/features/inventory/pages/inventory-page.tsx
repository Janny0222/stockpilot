import { ArrowDownToLine, ArrowLeftRight, ArrowUpFromLine, History, Search } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { DataTable, Pagination, type Column } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { InventoryMovementType, MovementDto, TransferListItemDto } from '@/lib/api/types'
import { formatQuantity } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { MOVEMENT_LABELS, useMovements, useTransfers } from '../api'
import { MovementTypeBadge, QuantityChange, WarehouseSelect } from '../components/inventory-parts'
import { StockTable } from '../components/stock-table'

const TABS = [
  { id: 'stock', label: 'Stock levels' },
  { id: 'movements', label: 'Movement history' },
  { id: 'transfers', label: 'Transfers' },
] as const
type TabId = (typeof TABS)[number]['id']

const dateTime = new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeStyle: 'short' })

export function InventoryPage() {
  const [params, setParams] = useSearchParams()
  const tab: TabId = (TABS.find((t) => t.id === params.get('tab'))?.id ?? 'stock') as TabId
  const canIn = useHasPermission(P.Inventory.StockIn)
  const canOut = useHasPermission(P.Inventory.StockOut)
  const canTransfer = useHasPermission(P.Inventory.Transfer)

  return (
    <>
      <PageHeader
        title="Inventory"
        description="What's on hand in each warehouse, and every movement that got it there."
        actions={
          <>
            {canIn && (
              <Button asChild variant="outline">
                <Link to="/inventory/stock-in"><ArrowDownToLine /> Stock in</Link>
              </Button>
            )}
            {canOut && (
              <Button asChild variant="outline">
                <Link to="/inventory/stock-out"><ArrowUpFromLine /> Stock out</Link>
              </Button>
            )}
            {canTransfer && (
              <Button asChild>
                <Link to="/inventory/transfers/new"><ArrowLeftRight /> Transfer</Link>
              </Button>
            )}
          </>
        }
      />

      <div role="tablist" aria-label="Inventory views" className="mb-4 inline-flex rounded-lg border bg-muted p-1">
        {TABS.map((t) => (
          <button
            key={t.id}
            role="tab"
            type="button"
            aria-selected={tab === t.id}
            onClick={() => setParams(t.id === 'stock' ? {} : { tab: t.id }, { replace: true })}
            className={cn(
              'rounded-md px-4 py-1.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring',
              tab === t.id ? 'bg-card text-foreground shadow-card' : 'text-muted-foreground hover:text-foreground',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>

      <Card role="tabpanel" className="gap-0 overflow-hidden py-0 shadow-card">
        {tab === 'stock' && <StockTable />}
        {tab === 'movements' && <MovementsTab />}
        {tab === 'transfers' && <TransfersTab />}
      </Card>
    </>
  )
}

function useUrlState() {
  const [params, setParams] = useSearchParams()
  const update = (changes: Record<string, string | null | undefined>) =>
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
  return [params, update] as const
}

const ALL = '__all'

function MovementsTab() {
  const navigate = useNavigate()
  const [params, update] = useUrlState()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const warehouseId = params.get('warehouse') ?? undefined
  const type = (params.get('type') as InventoryMovementType | null) ?? undefined
  const debouncedSearch = useDebouncedValue(search)

  const query = useMovements({ page, pageSize: 25, search: debouncedSearch || undefined, warehouseId, type })

  const columns: Column<MovementDto>[] = [
    { key: 'date', header: 'When', className: 'whitespace-nowrap tabular text-muted-foreground', cell: (m) => dateTime.format(new Date(m.occurredAt)) },
    { key: 'type', header: 'Movement', cell: (m) => <MovementTypeBadge type={m.type} /> },
    {
      key: 'item',
      header: 'Item',
      cell: (m) => (
        <div className="min-w-0">
          <p className="truncate font-medium">{m.productName}</p>
          <p className="truncate text-xs text-muted-foreground">{m.sku}</p>
        </div>
      ),
    },
    { key: 'warehouse', header: 'Warehouse', hideOnMobile: true, cell: (m) => m.warehouseCode },
    { key: 'change', header: 'Change', className: 'text-right', cell: (m) => <QuantityChange value={m.quantityChange} /> },
    { key: 'after', header: 'Balance', hideOnMobile: true, className: 'text-right tabular', cell: (m) => formatQuantity(m.quantityAfter) },
    {
      key: 'details',
      header: 'Details',
      hideOnMobile: true,
      cell: (m) => (
        <div className="max-w-56 text-sm">
          {m.reference && <p className="truncate font-medium">{m.reference}</p>}
          {(m.note || m.reason) && <p className="truncate text-muted-foreground">{[m.reason, m.note].filter(Boolean).join(' · ')}</p>}
        </div>
      ),
    },
    { key: 'by', header: 'By', hideOnMobile: true, className: 'whitespace-nowrap text-muted-foreground', cell: (m) => m.performedBy ?? 'System' },
  ]

  return (
    <>
      <div className="flex flex-col gap-3 border-b p-4 lg:flex-row lg:items-center">
        <div className="relative flex-1 lg:max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input type="search" value={search} onChange={(e) => update({ q: e.target.value || null, page: null })} placeholder="Item, SKU or reference" aria-label="Search movements" className="h-10 pl-9" />
        </div>
        <WarehouseSelect allowAll value={warehouseId} onChange={(v) => update({ warehouse: v, page: null })} className="lg:w-56" />
        <Select value={type ?? ALL} onValueChange={(v) => update({ type: v === ALL ? null : v, page: null })}>
          <SelectTrigger className="h-10 w-full bg-card lg:w-48" aria-label="Movement type">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All movements</SelectItem>
            {Object.entries(MOVEMENT_LABELS).map(([value, label]) => (
              <SelectItem key={value} value={value}>{label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Inventory movements"
            columns={columns}
            rows={query.data?.items}
            rowKey={(m) => m.id}
            isLoading={query.isPending}
            sort={null}
            onSortChange={() => undefined}
            onRowClick={(m) => (m.sourceType === 'StockTransfer' && m.sourceId ? navigate(`/inventory/transfers/${m.sourceId}`) : navigate(`/products/${m.productId}`))}
            empty={<EmptyState icon={History} title="No movements found" description="Stock-ins, stock-outs, adjustments and transfers appear here." />}
          />
          {query.data && query.data.totalCount > 0 && (
            <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={(p) => update({ page: p > 1 ? String(p) : null })} />
          )}
        </>
      )}
    </>
  )
}

function TransfersTab() {
  const navigate = useNavigate()
  const [params, update] = useUrlState()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const query = useTransfers({ page, pageSize: 20 })

  const columns: Column<TransferListItemDto>[] = [
    { key: 'number', header: 'Transfer', cell: (t) => <span className="font-semibold tabular">{t.number}</span> },
    {
      key: 'route',
      header: 'From → To',
      cell: (t) => (
        <span className="whitespace-nowrap">
          {t.sourceWarehouseCode} <span className="text-muted-foreground">→</span> {t.destinationWarehouseCode}
        </span>
      ),
    },
    { key: 'items', header: 'Items', hideOnMobile: true, className: 'tabular', cell: (t) => `${t.itemCount} (${formatQuantity(t.totalQuantity)} units)` },
    { key: 'date', header: 'Completed', className: 'whitespace-nowrap tabular text-muted-foreground', cell: (t) => dateTime.format(new Date(t.completedAt)) },
    { key: 'by', header: 'By', hideOnMobile: true, className: 'text-muted-foreground', cell: (t) => t.completedBy ?? '—' },
  ]

  return query.isError ? (
    <ErrorState error={query.error} onRetry={() => query.refetch()} />
  ) : (
    <>
      <DataTable
        caption="Stock transfers"
        columns={columns}
        rows={query.data?.items}
        rowKey={(t) => t.id}
        isLoading={query.isPending}
        sort={null}
        onSortChange={() => undefined}
        onRowClick={(t) => navigate(`/inventory/transfers/${t.id}`)}
        empty={<EmptyState icon={ArrowLeftRight} title="No transfers yet" description="Move stock between warehouses with the Transfer button." />}
      />
      {query.data && query.data.totalCount > 0 && (
        <Pagination page={query.data.page} pageSize={query.data.pageSize} totalCount={query.data.totalCount} onPageChange={(p) => update({ page: p > 1 ? String(p) : null })} />
      )}
    </>
  )
}
