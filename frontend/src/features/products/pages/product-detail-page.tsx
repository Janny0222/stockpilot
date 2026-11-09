import { Archive, ArchiveRestore, ArrowLeft, Pencil, Plus } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { toApiError } from '@/lib/api/errors'
import type { VariantDto } from '@/lib/api/types'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { useHasPermission } from '@/stores/auth-store'
import { useProduct, useSetProductArchived, useSetVariantArchived } from '../api'
import { EditDetailsDialog, VariantDialog } from '../components/product-dialogs'
import { ProductImagesCard } from '../components/product-images'

export function ProductDetailPage() {
  const { id = '' } = useParams()
  const query = useProduct(id)
  const currency = useCurrency()
  const canManage = useHasPermission(P.Products.Manage)
  const setArchived = useSetProductArchived(id)
  const setVariantArchived = useSetVariantArchived(id)

  const [editingDetails, setEditingDetails] = useState(false)
  const [variantDialog, setVariantDialog] = useState<{ open: boolean; variant?: VariantDto }>({ open: false })

  const back = (
    <Button asChild variant="outline">
      <Link to="/products">
        <ArrowLeft /> All products
      </Link>
    </Button>
  )

  if (query.isPending) {
    return (
      <div className="grid gap-6">
        <Skeleton className="h-9 w-72" />
        <div className="grid gap-6 lg:grid-cols-[20rem_1fr]">
          <Skeleton className="aspect-square" />
          <Skeleton className="h-80" />
        </div>
      </div>
    )
  }

  if (query.isError) {
    return (
      <>
        <PageHeader title="Product" actions={back} />
        <Card className="shadow-card">
          <ErrorState error={query.error} />
        </Card>
      </>
    )
  }

  const product = query.data
  const archived = product.status === 'Archived'
  const showCost = product.canViewCost

  const toggleVariant = (variant: VariantDto) =>
    setVariantArchived.mutate(
      { variantId: variant.id, archive: variant.isActive },
      {
        onSuccess: () => toast.success(`${variant.sku} ${variant.isActive ? 'archived' : 'restored'}.`),
        onError: (e) => toast.error(toApiError(e).message),
      },
    )

  return (
    <>
      <PageHeader
        title={product.name}
        description={
          <span className="flex flex-wrap items-center gap-2">
            {product.categoryName ?? 'Uncategorised'}
            <span aria-hidden="true">·</span>
            sold per {product.unitOfMeasure}
            {archived && <Badge variant="outline">Archived</Badge>}
          </span>
        }
        actions={
          <>
            {back}
            {canManage && !archived && (
              <Button variant="outline" onClick={() => setEditingDetails(true)}>
                <Pencil /> Edit details
              </Button>
            )}
            {canManage &&
              (archived ? (
                <Button
                  onClick={() =>
                    setArchived.mutate(false, { onSuccess: () => toast.success('Product restored.'), onError: (e) => toast.error(toApiError(e).message) })
                  }
                  disabled={setArchived.isPending}
                >
                  <ArchiveRestore /> Restore
                </Button>
              ) : (
                <ConfirmDialog
                  trigger={
                    <Button variant="outline" disabled={setArchived.isPending}>
                      <Archive /> Archive
                    </Button>
                  }
                  title={`Archive ${product.name}?`}
                  description="Archived products are hidden from lists and can't be sold or restocked. Their history is kept, and you can restore them any time."
                  confirmLabel="Archive product"
                  onConfirm={() =>
                    setArchived.mutate(true, { onSuccess: () => toast.success('Product archived.'), onError: (e) => toast.error(toApiError(e).message) })
                  }
                />
              ))}
          </>
        }
      />

      <div className="grid items-start gap-6 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <ProductImagesCard product={product} canManage={canManage} />

        <div className="grid gap-6">
          <Card className="shadow-card">
            <CardHeader className="flex flex-row items-center justify-between gap-3">
              <CardTitle>Variants</CardTitle>
              {canManage && !archived && (
                <Button size="sm" onClick={() => setVariantDialog({ open: true })}>
                  <Plus /> Add variant
                </Button>
              )}
            </CardHeader>
            <CardContent className="px-0">
              <Table>
                <caption className="sr-only">Variants of {product.name}</caption>
                <TableHeader>
                  <TableRow className="hover:bg-transparent">
                    <TableHead className="pl-6">SKU</TableHead>
                    <TableHead className="hidden md:table-cell">Barcode</TableHead>
                    {showCost && <TableHead className="text-right">Cost</TableHead>}
                    <TableHead className="text-right">Price</TableHead>
                    <TableHead className="hidden text-right sm:table-cell">Reorder at</TableHead>
                    {canManage && <TableHead className="pr-6 text-right">Actions</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {product.variants.map((v) => (
                    <TableRow key={v.id} className={cn(!v.isActive && 'text-muted-foreground')}>
                      <TableCell className="pl-6">
                        <p className="font-semibold text-foreground">{v.sku}</p>
                        <p className="text-xs text-muted-foreground">
                          {v.name ?? 'Standard'}
                          {!v.isActive && ' · archived'}
                        </p>
                      </TableCell>
                      <TableCell className="hidden md:table-cell tabular">{v.barcode ?? '—'}</TableCell>
                      {showCost && <TableCell className="text-right tabular">{formatMoney(v.costPrice, currency)}</TableCell>}
                      <TableCell className="text-right font-medium tabular">{formatMoney(v.sellingPrice, currency)}</TableCell>
                      <TableCell className="hidden text-right sm:table-cell tabular">{formatQuantity(v.reorderLevel)}</TableCell>
                      {canManage && (
                        <TableCell className="pr-6 text-right whitespace-nowrap">
                          {!archived && v.isActive && (
                            <Button variant="ghost" size="sm" onClick={() => setVariantDialog({ open: true, variant: v })}>
                              Edit
                            </Button>
                          )}
                          {!archived && (
                            <Button variant="ghost" size="sm" onClick={() => toggleVariant(v)} disabled={setVariantArchived.isPending}>
                              {v.isActive ? 'Archive' : 'Restore'}
                            </Button>
                          )}
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <Card className="shadow-card">
            <CardHeader>
              <CardTitle>About this product</CardTitle>
            </CardHeader>
            <CardContent>
              <p className={cn('max-w-prose whitespace-pre-line', !product.description && 'text-muted-foreground')}>
                {product.description ?? 'No description yet.'}
              </p>
            </CardContent>
          </Card>
        </div>
      </div>

      {canManage && (
        <>
          <EditDetailsDialog product={product} open={editingDetails} onOpenChange={setEditingDetails} />
          <VariantDialog
            productId={product.id}
            variant={variantDialog.variant}
            showCost={showCost}
            open={variantDialog.open}
            onOpenChange={(open) => setVariantDialog((d) => ({ ...d, open }))}
          />
        </>
      )}
    </>
  )
}
