import { Archive, ArchiveRestore, ArrowLeft, MapPin, Pencil, Star } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { StockTable } from '@/features/inventory/components/stock-table'
import { toApiError } from '@/lib/api/errors'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useWarehouse, useWarehouseAction } from '../api'
import { WarehouseDialog } from '../warehouse-dialog'

export function WarehouseDetailPage() {
  const { id = '' } = useParams()
  const query = useWarehouse(id)
  const canManage = useHasPermission(P.Warehouses.Manage)
  const action = useWarehouseAction(id)
  const [editing, setEditing] = useState(false)

  const back = (
    <Button asChild variant="outline">
      <Link to="/warehouses"><ArrowLeft /> All warehouses</Link>
    </Button>
  )

  if (query.isPending) {
    return (
      <div className="grid gap-6">
        <Skeleton className="h-9 w-64" />
        <Skeleton className="h-80 w-full" />
      </div>
    )
  }

  if (query.isError) {
    // A 404 here also covers "belongs to another company": the API deliberately can't tell them apart.
    return (
      <>
        <PageHeader title="Warehouse" actions={back} />
        <Card className="shadow-card"><ErrorState error={query.error} /></Card>
      </>
    )
  }

  const w = query.data
  const address = [w.addressLine, w.city, w.country].filter(Boolean).join(', ')
  const run = (a: 'archive' | 'restore' | 'default', success: string) =>
    action.mutate(a, { onSuccess: () => toast.success(success), onError: (e) => toast.error(toApiError(e).message) })

  return (
    <>
      <PageHeader
        title={w.name}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <span className="font-medium text-foreground">{w.code}</span>
            {address && (
              <span className="inline-flex items-center gap-1"><MapPin className="size-3.5" aria-hidden="true" />{address}</span>
            )}
            {w.isDefault && <Badge variant="secondary">Default</Badge>}
            {!w.isActive && <Badge variant="outline">Archived</Badge>}
          </span>
        }
        actions={
          <>
            {back}
            {canManage && w.isActive && (
              <Button variant="outline" onClick={() => setEditing(true)}><Pencil /> Edit</Button>
            )}
            {canManage && w.isActive && !w.isDefault && (
              <Button variant="outline" onClick={() => run('default', `${w.name} is now the default warehouse.`)} disabled={action.isPending}>
                <Star /> Make default
              </Button>
            )}
            {canManage && w.isActive && !w.isDefault && (
              <ConfirmDialog
                trigger={<Button variant="outline" disabled={action.isPending}><Archive /> Archive</Button>}
                title={`Archive ${w.name}?`}
                description="Archived warehouses can't receive or ship stock. A warehouse must be empty before it can be archived. Its history is kept."
                confirmLabel="Archive warehouse"
                onConfirm={() => run('archive', 'Warehouse archived.')}
              />
            )}
            {canManage && !w.isActive && (
              <Button onClick={() => run('restore', 'Warehouse restored.')} disabled={action.isPending}><ArchiveRestore /> Restore</Button>
            )}
          </>
        }
      />

      <Card className="gap-0 overflow-hidden py-0 shadow-card">
        <StockTable fixedWarehouseId={w.id} />
      </Card>

      {canManage && <WarehouseDialog warehouse={w} open={editing} onOpenChange={setEditing} />}
    </>
  )
}
