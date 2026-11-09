import { ArrowLeft, Boxes } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { EmptyState, ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useWarehouse } from '../api'

export function WarehouseDetailPage() {
  const { id = '' } = useParams()
  const query = useWarehouse(id)

  const back = (
    <Button asChild variant="outline">
      <Link to="/warehouses">
        <ArrowLeft /> All warehouses
      </Link>
    </Button>
  )

  if (query.isPending) {
    return (
      <div className="grid gap-6">
        <Skeleton className="h-9 w-64" />
        <Skeleton className="h-40 w-full max-w-2xl" />
      </div>
    )
  }

  if (query.isError) {
    // A 404 here also covers "belongs to another company": the API deliberately can't tell them apart.
    return (
      <>
        <PageHeader title="Warehouse" actions={back} />
        <Card className="shadow-card">
          <ErrorState error={query.error} />
        </Card>
      </>
    )
  }

  const w = query.data
  const address = [w.addressLine, w.city, w.country].filter(Boolean).join(', ')

  return (
    <>
      <PageHeader title={w.name} description={`Warehouse code ${w.code}`} actions={back} />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Details</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-4 text-sm">
              <Detail label="Code" value={<span className="tabular">{w.code}</span>} />
              <Detail label="Address" value={address || <span className="text-muted-foreground">Not set</span>} />
              <Detail
                label="Status"
                value={
                  <div className="flex gap-1.5">
                    {w.isDefault && <Badge variant="secondary">Default</Badge>}
                    <Badge variant="outline" className={w.isActive ? 'border-success/30 text-success' : ''}>
                      {w.isActive ? 'Active' : 'Archived'}
                    </Badge>
                  </div>
                }
              />
            </dl>
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader>
            <CardTitle>Stock in this warehouse</CardTitle>
          </CardHeader>
          <CardContent>
            <EmptyState
              icon={Boxes}
              className="py-8"
              title="Stock levels arrive with the Inventory module"
              description="Once products and stock movements are added, this shows what's on hand here."
            />
          </CardContent>
        </Card>
      </div>
    </>
  )
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[7rem_1fr] gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  )
}
