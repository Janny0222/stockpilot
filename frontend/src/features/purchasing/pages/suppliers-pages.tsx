import { Archive, ArchiveRestore, ArrowLeft, Mail, Pencil, Phone, Plus, Search, Truck } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { DataTable, Pagination, type Column, type SortState } from '@/components/shared/data-table'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { toApiError } from '@/lib/api/errors'
import type { SupplierDto } from '@/lib/api/types'
import { formatMoney, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useSetSupplierArchived, useSupplier, useSuppliers } from '../api'
import { SupplierDialog } from '../components/purchasing-parts'
import { PurchaseOrdersTable } from './purchase-orders-page'

export function SuppliersPage() {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canManage = useHasPermission(P.Suppliers.Manage)
  const [adding, setAdding] = useState(false)
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debounced = useDebouncedValue(search)
  const query = useSuppliers({
    page,
    pageSize: 20,
    search: debounced || undefined,
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

  const columns: Column<SupplierDto>[] = [
    {
      key: 'name',
      header: 'Supplier',
      sortField: 'name',
      cell: (s) => (
        <div className="min-w-0">
          <p className="truncate font-semibold">{s.name}</p>
          <p className="text-xs text-muted-foreground">{s.code}</p>
        </div>
      ),
    },
    {
      key: 'contact',
      header: 'Contact',
      hideOnMobile: true,
      cell: (s) => <span className="text-muted-foreground">{s.contactName ?? '—'}</span>,
    },
    {
      key: 'terms',
      header: 'Terms',
      hideOnMobile: true,
      cell: (s) => (s.paymentTermsDays === 0 ? 'Cash' : `${s.paymentTermsDays} days`),
    },
    {
      key: 'open',
      header: 'Open orders',
      className: 'tabular',
      cell: (s) => s.openOrders,
    },
    ...(query.data?.items.some((s) => s.totalPurchased !== null && s.totalPurchased !== undefined)
      ? [
          {
            key: 'spend',
            header: 'Received value',
            hideOnMobile: true,
            className: 'text-right tabular',
            cell: (s: SupplierDto) => formatMoney(s.totalPurchased, currency),
          },
        ]
      : []),
    {
      key: 'status',
      header: 'Status',
      cell: (s) =>
        s.isActive ? (
          <Badge variant="outline" className="border-success/30 text-success">
            Active
          </Badge>
        ) : (
          <Badge variant="outline">Archived</Badge>
        ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Suppliers"
        description="The companies you buy stock from."
        actions={
          canManage && (
            <Button onClick={() => setAdding(true)}>
              <Plus /> Add supplier
            </Button>
          )
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
              placeholder="Name, code or contact"
              aria-label="Search suppliers"
              className="h-10 pl-9"
            />
          </div>
        </div>
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Suppliers"
              columns={columns}
              rows={query.data?.items}
              rowKey={(s) => s.id}
              isLoading={query.isPending}
              sort={sort}
              onSortChange={(s) =>
                update({
                  sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null,
                  page: null,
                })
              }
              onRowClick={(s) => navigate(`/suppliers/${s.id}`)}
              empty={
                <EmptyState
                  icon={Truck}
                  title={debounced ? `No suppliers match "${debounced}"` : 'No suppliers yet'}
                  description="Add the companies you buy from to start ordering stock."
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
      {canManage && <SupplierDialog open={adding} onOpenChange={setAdding} />}
    </>
  )
}

export function SupplierDetailPage() {
  const { id = '' } = useParams()
  const query = useSupplier(id)
  const currency = useCurrency()
  const canManage = useHasPermission(P.Suppliers.Manage)
  const setArchived = useSetSupplierArchived(id)
  const [editing, setEditing] = useState(false)

  const back = (
    <Button asChild variant="outline">
      <Link to="/suppliers">
        <ArrowLeft /> All suppliers
      </Link>
    </Button>
  )

  if (query.isPending) return <Skeleton className="h-64 max-w-3xl" />
  if (query.isError) {
    return (
      <>
        <PageHeader title="Supplier" actions={back} />
        <Card className="shadow-card">
          <ErrorState error={query.error} />
        </Card>
      </>
    )
  }

  const s = query.data
  const toggle = (archive: boolean) =>
    setArchived.mutate(archive, {
      onSuccess: () => toast.success(archive ? 'Supplier archived.' : 'Supplier restored.'),
      onError: (e) => toast.error(toApiError(e).message),
    })

  return (
    <>
      <PageHeader
        title={s.name}
        description={
          <span className="flex items-center gap-2">
            {s.code}
            {!s.isActive && <Badge variant="outline">Archived</Badge>}
          </span>
        }
        actions={
          <>
            {back}
            {canManage && (
              <Button variant="outline" onClick={() => setEditing(true)}>
                <Pencil /> Edit
              </Button>
            )}
            {canManage &&
              (s.isActive ? (
                <ConfirmDialog
                  trigger={
                    <Button variant="outline">
                      <Archive /> Archive
                    </Button>
                  }
                  title={`Archive ${s.name}?`}
                  description="Archived suppliers can't be chosen on new purchase orders. Existing orders and history are kept."
                  confirmLabel="Archive supplier"
                  onConfirm={() => toggle(true)}
                />
              ) : (
                <Button onClick={() => toggle(false)}>
                  <ArchiveRestore /> Restore
                </Button>
              ))}
          </>
        }
      />
      <div className="grid items-start gap-6 xl:grid-cols-[20rem_minmax(0,1fr)]">
        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Contact</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-3 text-sm">
            <p className="font-medium">{s.contactName ?? 'No contact person'}</p>
            {s.email && (
              <a href={`mailto:${s.email}`} className="inline-flex items-center gap-2 text-primary hover:underline">
                <Mail className="size-4" />
                {s.email}
              </a>
            )}
            {s.phone && (
              <a href={`tel:${s.phone}`} className="inline-flex items-center gap-2 hover:underline">
                <Phone className="size-4 text-muted-foreground" />
                {s.phone}
              </a>
            )}
            {s.address && <p className="text-muted-foreground">{s.address}</p>}
            <dl className="mt-2 grid grid-cols-2 gap-3 border-t pt-3">
              <div>
                <dt className="text-muted-foreground">Payment terms</dt>
                <dd className="font-medium">{s.paymentTermsDays === 0 ? 'Cash' : `${s.paymentTermsDays} days`}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Open orders</dt>
                <dd className="font-medium tabular">{s.openOrders}</dd>
              </div>
              {s.totalPurchased !== null && s.totalPurchased !== undefined && (
                <div className="col-span-2">
                  <dt className="text-muted-foreground">Received value</dt>
                  <dd className="font-medium tabular">{formatMoney(s.totalPurchased, currency)}</dd>
                </div>
              )}
            </dl>
            {s.notes && <p className="border-t pt-3 whitespace-pre-line text-muted-foreground">{s.notes}</p>}
          </CardContent>
        </Card>
        <Card className="gap-0 overflow-hidden py-0 shadow-card">
          <div className="border-b px-6 py-4">
            <h2 className="font-semibold">Purchase history</h2>
          </div>
          <PurchaseOrdersTable supplierId={s.id} />
        </Card>
      </div>
      {canManage && <SupplierDialog supplier={s} open={editing} onOpenChange={setEditing} />}
    </>
  )
}
