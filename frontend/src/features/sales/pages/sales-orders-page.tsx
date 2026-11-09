import { Plus, Receipt, Search, Users } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { ReceiptProgress } from '@/features/purchasing/components/purchasing-parts'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { SalesOrderListItemDto, SalesOrderStatus } from '@/lib/api/types'
import { formatDate, formatMoney, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { SO_STATUS, useSalesOrders } from '../api'
import { SoStatusBadge } from '../components/sales-parts'

const STATUS_FILTERS: (SalesOrderStatus | 'All')[] = ['All', 'Draft', 'Confirmed', 'PartiallyFulfilled', 'Fulfilled', 'Cancelled']

export function SalesOrdersPage() {
  const canCreate = useHasPermission(P.Sales.Create)
  const canSeeCustomers = useHasPermission(P.Customers.View)
  return (
    <>
      <PageHeader
        title="Sales orders"
        description="Customer orders: confirming one reserves its stock, shipping it takes the stock out."
        actions={
          <>
            {canSeeCustomers && (
              <Button asChild variant="outline">
                <Link to="/customers">
                  <Users /> Customers
                </Link>
              </Button>
            )}
            {canCreate && (
              <Button asChild>
                <Link to="/sales/new">
                  <Plus /> New sales order
                </Link>
              </Button>
            )}
          </>
        }
      />
      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <SalesOrdersTable withFilters />
      </Card>
    </>
  )
}

/** Shared by the Sales page (with filters) and a customer's page (fixed customer). */
export function SalesOrdersTable({ customerId, withFilters = false }: { customerId?: string; withFilters?: boolean }) {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canCreate = useHasPermission(P.Sales.Create)
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const status = (params.get('status') as SalesOrderStatus | null) ?? undefined
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debounced = useDebouncedValue(search)

  const query = useSalesOrders({
    page,
    pageSize: 20,
    customerId,
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

  const columns: Column<SalesOrderListItemDto>[] = [
    { key: 'number', header: 'Order', sortField: 'number', cell: (o) => <span className="font-semibold tabular">{o.number}</span> },
    ...(customerId
      ? []
      : [{ key: 'customer', header: 'Customer', cell: (o: SalesOrderListItemDto) => <span className="block max-w-56 truncate">{o.customerName}</span> }]),
    { key: 'status', header: 'Status', cell: (o) => <SoStatusBadge status={o.status} /> },
    { key: 'date', header: 'Date', sortField: 'date', hideOnMobile: true, className: 'whitespace-nowrap tabular', cell: (o) => formatDate(o.orderDate) },
    { key: 'warehouse', header: 'Ships from', hideOnMobile: true, cell: (o) => o.warehouseCode },
    {
      key: 'progress',
      header: 'Shipped',
      hideOnMobile: true,
      cell: (o) =>
        o.status === 'Draft' || (o.status === 'Cancelled' && o.quantityShipped === 0) ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <ReceiptProgress received={o.quantityShipped} ordered={o.quantityOrdered} label="Shipped" />
        ),
    },
    { key: 'total', header: 'Total', sortField: 'total', className: 'text-right tabular', cell: (o) => formatMoney(o.total, currency) },
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
              placeholder="SO number or customer"
              aria-label="Search sales orders"
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
                  {s === 'All' ? 'All' : SO_STATUS[s].label}
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
            caption="Sales orders"
            columns={columns}
            rows={query.data?.items}
            rowKey={(o) => o.id}
            isLoading={query.isPending}
            sort={sort}
            onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
            onRowClick={(o) => navigate(`/sales/${o.id}`)}
            empty={
              <EmptyState
                icon={Receipt}
                title={status || debounced ? 'No orders match these filters' : 'No sales orders yet'}
                description={customerId ? 'Orders from this customer will appear here.' : 'Create a sales order when a customer buys.'}
                action={
                  !customerId &&
                  canCreate &&
                  !status &&
                  !debounced && (
                    <Button asChild>
                      <Link to="/sales/new">
                        <Plus /> New sales order
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
