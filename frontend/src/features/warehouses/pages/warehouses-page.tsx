import { MapPin, Search, Warehouse } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { WarehouseDto } from '@/lib/api/types'
import { useAuthStore } from '@/stores/auth-store'
import { useWarehouses } from '../api'

const PAGE_SIZE = 10

const columns: Column<WarehouseDto>[] = [
  {
    key: 'name',
    header: 'Warehouse',
    sortField: 'name',
    cell: (w) => (
      <div className="flex items-center gap-3">
        <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-secondary text-secondary-foreground">
          <Warehouse className="size-4" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <p className="truncate font-semibold">{w.name}</p>
          <p className="text-xs text-muted-foreground">{w.code}</p>
        </div>
      </div>
    ),
  },
  {
    key: 'code',
    header: 'Code',
    sortField: 'code',
    hideOnMobile: true,
    cell: (w) => <span className="font-medium tabular">{w.code}</span>,
  },
  {
    key: 'location',
    header: 'Location',
    hideOnMobile: true,
    cell: (w) =>
      w.city || w.country ? (
        <span className="inline-flex items-center gap-1.5 text-muted-foreground">
          <MapPin className="size-3.5" aria-hidden="true" />
          {[w.city, w.country].filter(Boolean).join(', ')}
        </span>
      ) : (
        <span className="text-muted-foreground">Not set</span>
      ),
  },
  {
    key: 'status',
    header: 'Status',
    cell: (w) => (
      <div className="flex flex-wrap gap-1.5">
        {w.isDefault && <Badge variant="secondary">Default</Badge>}
        <Badge variant="outline" className={w.isActive ? 'border-success/30 text-success' : 'text-muted-foreground'}>
          {w.isActive ? 'Active' : 'Archived'}
        </Badge>
      </div>
    ),
  },
]

/**
 * List state (page, sort, search) lives in the URL: refresh, back/forward and shared links
 * all reproduce exactly the same view.
 */
export function WarehousesPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const scope = useAuthStore((s) => s.user?.warehouseScope)

  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debouncedSearch = useDebouncedValue(search)

  const query = useWarehouses({
    page,
    pageSize: PAGE_SIZE,
    search: debouncedSearch || undefined,
    sort: sort ? `${sort.field}:${sort.desc ? 'desc' : 'asc'}` : undefined,
  })

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

  return (
    <>
      <PageHeader
        title="Warehouses"
        description={
          scope?.restricted
            ? 'Showing only the warehouses assigned to you.'
            : 'Every location where your company keeps stock.'
        }
      />

      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <div className="border-b p-4">
          <div className="relative max-w-sm">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              type="search"
              value={search}
              onChange={(e) => update({ q: e.target.value || null, page: null })}
              placeholder="Search by name or code"
              aria-label="Search warehouses"
              className="h-10 pl-9"
            />
          </div>
        </div>

        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Warehouses"
              columns={columns}
              rows={query.data?.items}
              rowKey={(w) => w.id}
              isLoading={query.isPending}
              sort={sort}
              onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
              onRowClick={(w) => navigate(`/warehouses/${w.id}`)}
              empty={
                <EmptyState
                  icon={Warehouse}
                  title={search ? `No warehouses match "${search}"` : 'No warehouses yet'}
                  description={search ? 'Try a different name or code.' : 'Warehouses you have access to will appear here.'}
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
    </>
  )
}
