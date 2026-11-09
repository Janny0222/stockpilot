import { FolderTree, Package, Plus, Search } from 'lucide-react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { ProductListItemDto, ProductStatus } from '@/lib/api/types'
import { formatPriceRange, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { useProducts } from '../api'
import { CategorySelect, ProductImage } from '../components/product-parts'

const PAGE_SIZE = 15

export function ProductsPage() {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canManage = useHasPermission(P.Products.Manage)
  const [params, setParams] = useSearchParams()

  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const categoryId = params.get('category') ?? undefined
  const status: ProductStatus = params.get('status') === 'Archived' ? 'Archived' : 'Active'
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debouncedSearch = useDebouncedValue(search)

  const query = useProducts({
    page,
    pageSize: PAGE_SIZE,
    search: debouncedSearch || undefined,
    categoryId,
    status,
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

  const columns: Column<ProductListItemDto>[] = [
    {
      key: 'name',
      header: 'Product',
      sortField: 'name',
      cell: (p) => (
        <div className="flex items-center gap-3">
          <ProductImage productId={p.id} imageId={p.primaryImageId} alt="" className="size-10" />
          <div className="min-w-0">
            <p className="truncate font-semibold">{p.name}</p>
            <p className="truncate text-xs text-muted-foreground">
              {p.sku}
              {p.variantCount > 1 && ` +${p.variantCount - 1} more`}
            </p>
          </div>
        </div>
      ),
    },
    {
      key: 'category',
      header: 'Category',
      hideOnMobile: true,
      cell: (p) => p.categoryName ?? <span className="text-muted-foreground">Uncategorised</span>,
    },
    {
      key: 'variants',
      header: 'Variants',
      hideOnMobile: true,
      className: 'tabular',
      cell: (p) => p.variantCount,
    },
    {
      key: 'price',
      header: 'Price',
      sortField: 'price',
      className: 'tabular whitespace-nowrap',
      cell: (p) => (
        <>
          {formatPriceRange(p.minPrice, p.maxPrice, currency)}
          <span className="text-muted-foreground"> / {p.unitOfMeasure}</span>
        </>
      ),
    },
  ]

  const filtered = Boolean(debouncedSearch || categoryId)

  return (
    <>
      <PageHeader
        title="Products"
        description="Everything your company stocks and sells."
        actions={
          <>
            <Button asChild variant="outline">
              <Link to="/products/categories">
                <FolderTree /> Categories
              </Link>
            </Button>
            {canManage && (
              <Button asChild>
                <Link to="/products/new">
                  <Plus /> Add product
                </Link>
              </Button>
            )}
          </>
        }
      />

      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <div className="flex flex-col gap-3 border-b p-4 lg:flex-row lg:items-center">
          <div className="relative flex-1 lg:max-w-sm">
            <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <Input
              type="search"
              value={search}
              onChange={(e) => update({ q: e.target.value || null, page: null })}
              placeholder="Search name, SKU or barcode"
              aria-label="Search products"
              className="h-10 pl-9"
            />
          </div>
          <CategorySelect
            value={categoryId}
            onChange={(v) => update({ category: v, page: null })}
            placeholder="All categories"
            className="lg:w-56"
          />
          <div role="group" aria-label="Status" className="inline-flex rounded-lg border bg-muted p-1 lg:ml-auto">
            {(['Active', 'Archived'] as const).map((s) => (
              <button
                key={s}
                type="button"
                aria-pressed={status === s}
                onClick={() => update({ status: s === 'Active' ? null : s, page: null })}
                className={cn(
                  'rounded-md px-3.5 py-1.5 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring',
                  status === s ? 'bg-card text-foreground shadow-card' : 'text-muted-foreground hover:text-foreground',
                )}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Products"
              columns={columns}
              rows={query.data?.items}
              rowKey={(p) => p.id}
              isLoading={query.isPending}
              sort={sort}
              onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
              onRowClick={(p) => navigate(`/products/${p.id}`)}
              empty={
                filtered ? (
                  <EmptyState icon={Search} title="No products match these filters" description="Try another search term or category." />
                ) : status === 'Archived' ? (
                  <EmptyState icon={Package} title="No archived products" description="Products you archive are kept here with their history." />
                ) : (
                  <EmptyState
                    icon={Package}
                    title="No products yet"
                    description="Add the items you stock: each can have several variants with their own SKU and price."
                    action={
                      canManage && (
                        <Button asChild>
                          <Link to="/products/new">
                            <Plus /> Add your first product
                          </Link>
                        </Button>
                      )
                    }
                  />
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
      </Card>
    </>
  )
}
