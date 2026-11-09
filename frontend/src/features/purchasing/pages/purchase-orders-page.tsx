import { Plus, Search, ShoppingCart, Truck } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { PurchaseOrderListItemDto, PurchaseOrderStatus } from '@/lib/api/types'
import { formatDate, formatMoney, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { PO_STATUS, usePurchaseOrders } from '../api'
import { PoStatusBadge, ReceiptProgress } from '../components/purchasing-parts'

const STATUS_FILTERS: (PurchaseOrderStatus | 'All')[] = ['All', 'Draft', 'Submitted', 'Approved', 'PartiallyReceived', 'Received', 'Cancelled']

export function PurchaseOrdersPage() {
  const canManage = useHasPermission(P.Purchases.Manage)
  return (
    <>
      <PageHeader
        title="Purchase orders"
        description="Orders to your suppliers, from draft to delivery."
        actions={
          <>
            <Button asChild variant="outline">
              <Link to="/suppliers">
                <Truck /> Suppliers
              </Link>
            </Button>
            {canManage && (
              <Button asChild>
                <Link to="/purchases/new">
                  <Plus /> New purchase order
                </Link>
              </Button>
            )}
          </>
        }
      />
      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <PurchaseOrdersTable withFilters />
      </Card>
    </>
  )
}

/** Shared by the Purchases page (with filters) and a supplier's page (fixed supplier, compact). */
export function PurchaseOrdersTable({ supplierId, withFilters = false }: { supplierId?: string; withFilters?: boolean }) {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canManage = useHasPermission(P.Purchases.Manage)
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const status = (params.get('status') as PurchaseOrderStatus | null) ?? undefined
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debounced = useDebouncedValue(search)

  const query = usePurchaseOrders({
    page,
    pageSize: 20,
    supplierId,
    status,
    search: debounced || undefined,
    sort: sort ? `${sort.field}:${sort.desc ? 'desc' : 'asc'}` : undefined,
  })

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

  const showTotal = query.data?.items.some((p) => p.total !== null && p.total !== undefined)
  const columns: Column<PurchaseOrderListItemDto>[] = [
    {
      key: 'number',
      header: 'Order',
      sortField: 'number',
      cell: (p) => <span className="font-semibold tabular">{p.number}</span>,
    },
    ...(supplierId
      ? []
      : [
          {
            key: 'supplier',
            header: 'Supplier',
            cell: (p: PurchaseOrderListItemDto) => <span className="block max-w-56 truncate">{p.supplierName}</span>,
          },
        ]),
    {
      key: 'status',
      header: 'Status',
      cell: (p) => <PoStatusBadge status={p.status} />,
    },
    {
      key: 'warehouse',
      header: 'Deliver to',
      hideOnMobile: true,
      cell: (p) => p.warehouseCode,
    },
    {
      key: 'expected',
      header: 'Expected',
      sortField: 'expected',
      hideOnMobile: true,
      className: 'whitespace-nowrap tabular',
      cell: (p) => formatDate(p.expectedDate),
    },
    {
      key: 'progress',
      header: 'Received',
      hideOnMobile: true,
      cell: (p) =>
        p.status === 'Draft' || p.status === 'Cancelled' ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <ReceiptProgress received={p.quantityReceived} ordered={p.quantityOrdered} />
        ),
    },
    ...(showTotal
      ? [
          {
            key: 'total',
            header: 'Total',
            sortField: 'total',
            className: 'text-right tabular',
            cell: (p: PurchaseOrderListItemDto) => formatMoney(p.total, currency),
          },
        ]
      : []),
  ]

  return (
    <>
      {withFilters && (
        <div className="flex flex-col gap-3 border-b p-4">
          <div className="relative max-w-sm">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              type="search"
              value={search}
              onChange={(e) => update({ q: e.target.value || null, page: null })}
              placeholder="PO number or supplier"
              aria-label="Search purchase orders"
              className="h-10 pl-9"
            />
          </div>
          <div role="group" aria-label="Filter by status" className="flex flex-wrap gap-2">
            {STATUS_FILTERS.map((s) => {
              const active = (s === 'All' && !status) || s === status
              return (
                <button
                  key={s}
                  type="button"
                  aria-pressed={active}
                  onClick={() => update({ status: s === 'All' ? null : s, page: null })}
                  className={cn(
                    'rounded-full border px-3 py-1 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring',
                    active ? 'border-primary bg-primary text-primary-foreground' : 'bg-card text-muted-foreground hover:text-foreground',
                  )}
                >
                  {s === 'All' ? 'All' : PO_STATUS[s].label}
                </button>
              )
            })}
          </div>
        </div>
      )}
      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Purchase orders"
            columns={columns}
            rows={query.data?.items}
            rowKey={(p) => p.id}
            isLoading={query.isPending}
            sort={sort}
            onSortChange={(s) =>
              update({
                sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null,
                page: null,
              })
            }
            onRowClick={(p) => navigate(`/purchases/${p.id}`)}
            empty={
              <EmptyState
                icon={ShoppingCart}
                title={status || debounced ? 'No orders match these filters' : 'No purchase orders yet'}
                description={supplierId ? 'Orders to this supplier will appear here.' : 'Create a purchase order to restock from a supplier.'}
                action={
                  !supplierId &&
                  canManage &&
                  !status &&
                  !debounced && (
                    <Button asChild>
                      <Link to="/purchases/new">
                        <Plus /> New purchase order
                      </Link>
                    </Button>
                  )
                }
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
    </>
  )
}
