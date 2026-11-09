import { Boxes, Search } from 'lucide-react'
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { StockLevelDto } from '@/lib/api/types'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { useStock } from '../api'
import { AdjustDialog } from './adjust-dialog'
import { StockStatusBadge, WarehouseSelect } from './inventory-parts'

/**
 * Stock levels with filters kept in the URL. Used on the Inventory page (all warehouses) and on a
 * warehouse's detail page (fixedWarehouseId).
 */
export function StockTable({ fixedWarehouseId }: { fixedWarehouseId?: string }) {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canAdjust = useHasPermission(P.Inventory.Adjust)
  const canViewCost = useHasPermission(P.Inventory.ViewCost)
  const [params, setParams] = useSearchParams()
  const [adjusting, setAdjusting] = useState<StockLevelDto | null>(null)

  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const warehouseId = fixedWarehouseId ?? params.get('warehouse') ?? undefined
  const lowStockOnly = params.get('low') === '1'
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debouncedSearch = useDebouncedValue(search)

  const query = useStock({
    page,
    pageSize: 20,
    search: debouncedSearch || undefined,
    warehouseId,
    lowStockOnly: lowStockOnly || undefined,
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

  const columns: Column<StockLevelDto>[] = [
    {
      key: 'name',
      header: 'Item',
      sortField: 'name',
      cell: (r) => (
        <div className="min-w-0">
          <p className="truncate font-semibold">{r.productName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {r.sku}
            {r.variantName && ` · ${r.variantName}`}
          </p>
        </div>
      ),
    },
    ...(fixedWarehouseId
      ? []
      : [{ key: 'warehouse', header: 'Warehouse', hideOnMobile: true, cell: (r: StockLevelDto) => <span title={r.warehouseName}>{r.warehouseCode}</span> }]),
    {
      key: 'available',
      header: 'Available',
      sortField: 'available',
      className: 'text-right tabular',
      cell: (r) => (
        <span className={cn('font-semibold', r.status === 'Out' && 'text-destructive', r.status === 'Low' && 'text-warning')}>
          {formatQuantity(r.available)} <span className="font-normal text-muted-foreground">{r.unitOfMeasure}</span>
        </span>
      ),
    },
    { key: 'onhand', header: 'On hand', hideOnMobile: true, className: 'text-right tabular', cell: (r) => formatQuantity(r.onHand) },
    { key: 'reorder', header: 'Reorder at', hideOnMobile: true, className: 'text-right tabular text-muted-foreground', cell: (r) => formatQuantity(r.reorderLevel) },
    ...(canViewCost
      ? [{ key: 'value', header: 'Value', sortField: 'value', hideOnMobile: true, className: 'text-right tabular', cell: (r: StockLevelDto) => formatMoney(r.stockValue, currency) }]
      : []),
    { key: 'status', header: 'Status', cell: (r) => <StockStatusBadge status={r.status} /> },
    ...(canAdjust
      ? [{
          key: 'actions',
          header: '',
          className: 'text-right',
          cell: (r: StockLevelDto) => (
            <Button variant="ghost" size="sm" onClick={(e) => { e.stopPropagation(); setAdjusting(r) }}>
              Adjust
            </Button>
          ),
        }]
      : []),
  ]

  return (
    <>
      <div className="flex flex-col gap-3 border-b p-4 lg:flex-row lg:items-center">
        <div className="relative flex-1 lg:max-w-sm">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            type="search"
            value={search}
            onChange={(e) => update({ q: e.target.value || null, page: null })}
            placeholder="Search name, SKU or barcode"
            aria-label="Search stock"
            className="h-10 pl-9"
          />
        </div>
        {!fixedWarehouseId && <WarehouseSelect allowAll value={warehouseId} onChange={(v) => update({ warehouse: v, page: null })} className="lg:w-60" />}
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm font-medium lg:ml-auto">
          <input
            type="checkbox"
            checked={lowStockOnly}
            onChange={(e) => update({ low: e.target.checked ? '1' : null, page: null })}
            className="size-4 accent-primary"
          />
          Low or out of stock only
        </label>
      </div>

      {query.isError ? (
        <ErrorState error={query.error} onRetry={() => query.refetch()} />
      ) : (
        <>
          <DataTable
            caption="Stock levels"
            columns={columns}
            rows={query.data?.items}
            rowKey={(r) => `${r.warehouseId}:${r.variantId}`}
            isLoading={query.isPending}
            sort={sort}
            onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
            onRowClick={(r) => navigate(`/products/${r.productId}`)}
            empty={
              lowStockOnly ? (
                <EmptyState icon={Boxes} title="Nothing is running low" description="Every item is above its reorder level." />
              ) : debouncedSearch ? (
                <EmptyState icon={Search} title={`No stock matches "${debouncedSearch}"`} description="Try another name or SKU." />
              ) : (
                <EmptyState icon={Boxes} title="No stock recorded yet" description="Record a stock-in to start tracking quantities." />
              )
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

      <AdjustDialog row={adjusting} open={adjusting !== null} onOpenChange={(open) => !open && setAdjusting(null)} />
    </>
  )
}
