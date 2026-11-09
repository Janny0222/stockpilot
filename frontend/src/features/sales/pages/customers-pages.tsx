import { Archive, ArchiveRestore, ArrowLeft, Mail, Pencil, Phone, Plus, Search, Users } from 'lucide-react'
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
import type { CustomerDto } from '@/lib/api/types'
import { formatMoney, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useCustomer, useCustomers, useSetCustomerArchived } from '../api'
import { CustomerDialog } from '../components/sales-parts'
import { SalesOrdersTable } from './sales-orders-page'

const terms = (days: number) => (days === 0 ? 'Cash' : `${days} days`)

export function CustomersPage() {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canManage = useHasPermission(P.Customers.Manage)
  const [adding, setAdding] = useState(false)
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number(params.get('page')) || 1)
  const search = params.get('q') ?? ''
  const sortParam = params.get('sort')
  const sort: SortState = sortParam ? { field: sortParam.split(':')[0]!, desc: sortParam.endsWith(':desc') } : null
  const debounced = useDebouncedValue(search)
  const query = useCustomers({
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

  const columns: Column<CustomerDto>[] = [
    {
      key: 'name',
      header: 'Customer',
      sortField: 'name',
      cell: (c) => (
        <div className="min-w-0">
          <p className="truncate font-semibold">{c.name}</p>
          <p className="text-xs text-muted-foreground">{c.code}</p>
        </div>
      ),
    },
    { key: 'contact', header: 'Contact', hideOnMobile: true, cell: (c) => <span className="text-muted-foreground">{c.contactName ?? '—'}</span> },
    { key: 'terms', header: 'Terms', hideOnMobile: true, cell: (c) => terms(c.paymentTermsDays) },
    { key: 'open', header: 'Open orders', className: 'tabular', cell: (c) => c.openOrders },
    { key: 'sales', header: 'Total sales', hideOnMobile: true, className: 'text-right tabular', cell: (c) => formatMoney(c.totalSales, currency) },
    {
      key: 'status',
      header: 'Status',
      cell: (c) =>
        c.isActive ? (
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
        title="Customers"
        description="The companies and people you sell to."
        actions={
          canManage && (
            <Button onClick={() => setAdding(true)}>
              <Plus /> Add customer
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
              aria-label="Search customers"
              className="h-10 pl-9"
            />
          </div>
        </div>
        {query.isError ? (
          <ErrorState error={query.error} onRetry={() => query.refetch()} />
        ) : (
          <>
            <DataTable
              caption="Customers"
              columns={columns}
              rows={query.data?.items}
              rowKey={(c) => c.id}
              isLoading={query.isPending}
              sort={sort}
              onSortChange={(s) => update({ sort: s ? `${s.field}:${s.desc ? 'desc' : 'asc'}` : null, page: null })}
              onRowClick={(c) => navigate(`/customers/${c.id}`)}
              empty={
                <EmptyState
                  icon={Users}
                  title={debounced ? `No customers match "${debounced}"` : 'No customers yet'}
                  description="Add the people and companies you sell to, then create sales orders for them."
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
      {canManage && <CustomerDialog open={adding} onOpenChange={setAdding} />}
    </>
  )
}

export function CustomerDetailPage() {
  const { id = '' } = useParams()
  const query = useCustomer(id)
  const currency = useCurrency()
  const canManage = useHasPermission(P.Customers.Manage)
  const canSeeSales = useHasPermission(P.Sales.View)
  const setArchived = useSetCustomerArchived(id)
  const [editing, setEditing] = useState(false)

  const back = (
    <Button asChild variant="outline">
      <Link to="/customers">
        <ArrowLeft /> All customers
      </Link>
    </Button>
  )

  if (query.isPending) return <Skeleton className="h-64 max-w-3xl" />
  if (query.isError) {
    return (
      <>
        <PageHeader title="Customer" actions={back} />
        <Card className="shadow-card">
          <ErrorState error={query.error} />
        </Card>
      </>
    )
  }

  const c = query.data
  const toggle = (archive: boolean) =>
    setArchived.mutate(archive, {
      onSuccess: () => toast.success(archive ? 'Customer archived.' : 'Customer restored.'),
      onError: (e) => toast.error(toApiError(e).message),
    })

  return (
    <>
      <PageHeader
        title={c.name}
        description={
          <span className="flex items-center gap-2">
            {c.code}
            {!c.isActive && <Badge variant="outline">Archived</Badge>}
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
              (c.isActive ? (
                <ConfirmDialog
                  trigger={
                    <Button variant="outline">
                      <Archive /> Archive
                    </Button>
                  }
                  title={`Archive ${c.name}?`}
                  description="Archived customers can't be chosen on new sales orders. Existing orders and history are kept."
                  confirmLabel="Archive customer"
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
            <p className="font-medium">{c.contactName ?? 'No contact person'}</p>
            {c.email && (
              <a href={`mailto:${c.email}`} className="inline-flex items-center gap-2 text-primary hover:underline">
                <Mail className="size-4" />
                {c.email}
              </a>
            )}
            {c.phone && (
              <a href={`tel:${c.phone}`} className="inline-flex items-center gap-2 hover:underline">
                <Phone className="size-4 text-muted-foreground" />
                {c.phone}
              </a>
            )}
            {c.address && <p className="text-muted-foreground">{c.address}</p>}
            <dl className="mt-2 grid grid-cols-2 gap-3 border-t pt-3">
              <div>
                <dt className="text-muted-foreground">Payment terms</dt>
                <dd className="font-medium">{terms(c.paymentTermsDays)}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Open orders</dt>
                <dd className="font-medium tabular">{c.openOrders}</dd>
              </div>
              <div className="col-span-2">
                <dt className="text-muted-foreground">Total sales (shipped)</dt>
                <dd className="font-medium tabular">{formatMoney(c.totalSales, currency)}</dd>
              </div>
            </dl>
            {c.notes && <p className="border-t pt-3 whitespace-pre-line text-muted-foreground">{c.notes}</p>}
          </CardContent>
        </Card>
        {canSeeSales && (
          <Card className="gap-0 overflow-hidden py-0 shadow-card">
            <div className="border-b px-6 py-4">
              <h2 className="font-semibold">Order history</h2>
            </div>
            <SalesOrdersTable customerId={c.id} />
          </Card>
        )}
      </div>
      {canManage && <CustomerDialog customer={c} open={editing} onOpenChange={setEditing} />}
    </>
  )
}
