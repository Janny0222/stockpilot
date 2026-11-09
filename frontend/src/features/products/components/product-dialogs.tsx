import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle } from 'lucide-react'
import { useEffect } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { FormAlert } from '@/features/auth/components/form-alert'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import type { ProductDetailDto, VariantDto } from '@/lib/api/types'
import { useAddVariant, useUpdateProduct, useUpdateVariant } from '../api'
import { blankToNull, productDetailsSchema, UNITS, variantSchema, type ProductDetailsValues, type VariantValues } from '../schemas'
import { CategorySelect, VariantFields } from './product-parts'

const CONFLICT_MESSAGE = 'Someone else changed this product while you were editing. The latest version is now loaded.'

export function EditDetailsDialog({ product, open, onOpenChange }: { product: ProductDetailDto; open: boolean; onOpenChange: (open: boolean) => void }) {
  const update = useUpdateProduct(product.id)
  const form = useForm<ProductDetailsValues>({ resolver: zodResolver(productDetailsSchema) })

  useEffect(() => {
    if (open) {
      form.reset({
        name: product.name,
        description: product.description ?? '',
        categoryId: product.categoryId ?? undefined,
        unitOfMeasure: product.unitOfMeasure as ProductDetailsValues['unitOfMeasure'],
      })
    }
  }, [open, product, form])

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await update.mutateAsync({
        name: values.name,
        description: blankToNull(values.description),
        categoryId: values.categoryId ?? null,
        unitOfMeasure: values.unitOfMeasure,
        version: product.version, // the version this user loaded: a newer save by someone else → 409
      })
      toast.success('Product details saved.')
      onOpenChange(false)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'CONCURRENCY_CONFLICT') {
        toast.error(CONFLICT_MESSAGE)
        onOpenChange(false)
      } else if (!applyAllFieldErrors(error, form.setError)) {
        form.setError('root', { message: error.message })
      }
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Edit product details</DialogTitle>
          <DialogDescription>Name, category, unit and description. Prices are set per variant.</DialogDescription>
        </DialogHeader>
        <form id="edit-details" noValidate onSubmit={onSubmit} className="grid gap-4">
          {form.formState.errors.root?.message && <FormAlert tone="error">{form.formState.errors.root.message}</FormAlert>}
          <FormField label="Product name" registration={form.register('name')} error={form.formState.errors.name} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Controller
              control={form.control}
              name="categoryId"
              render={({ field }) => <CategorySelect id="edit-category" label="Category" value={field.value} onChange={field.onChange} />}
            />
            <div className="grid gap-1.5">
              <Label htmlFor="edit-unit">Unit</Label>
              <Controller
                control={form.control}
                name="unitOfMeasure"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="edit-unit" className="h-10 w-full bg-card">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {UNITS.map((u) => (
                        <SelectItem key={u} value={u}>
                          {u}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="edit-description">Description</Label>
            <Textarea id="edit-description" rows={3} className="bg-card" {...form.register('description')} />
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="edit-details" disabled={update.isPending}>
            {update.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Save details
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Add (variant = undefined) or edit a variant. */
export function VariantDialog({
  productId,
  variant,
  showCost,
  open,
  onOpenChange,
}: {
  productId: string
  variant?: VariantDto
  showCost: boolean
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const add = useAddVariant(productId)
  const update = useUpdateVariant(productId)
  const pending = add.isPending || update.isPending
  const form = useForm<VariantValues>({ resolver: zodResolver(variantSchema) })

  useEffect(() => {
    if (open) {
      form.reset({
        sku: variant?.sku ?? '',
        name: variant?.name ?? '',
        barcode: variant?.barcode ?? '',
        costPrice: variant ? (variant.costPrice ?? 0) : showCost ? Number.NaN : 0,
        sellingPrice: variant?.sellingPrice ?? Number.NaN,
        reorderLevel: variant?.reorderLevel ?? 0,
      })
    }
  }, [open, variant, showCost, form])

  const onSubmit = form.handleSubmit(async (values) => {
    const body = {
      sku: values.sku,
      name: blankToNull(values.name),
      barcode: blankToNull(values.barcode),
      costPrice: values.costPrice,
      sellingPrice: values.sellingPrice,
      reorderLevel: values.reorderLevel,
    }
    try {
      if (variant) {
        await update.mutateAsync({ variantId: variant.id, body: { ...body, version: variant.version } })
        toast.success(`Variant ${values.sku.toUpperCase()} saved.`)
      } else {
        await add.mutateAsync(body)
        toast.success(`Variant ${values.sku.toUpperCase()} added.`)
      }
      onOpenChange(false)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'CONCURRENCY_CONFLICT') {
        toast.error(CONFLICT_MESSAGE)
        onOpenChange(false)
      } else if (error.code === 'SKU_TAKEN') {
        form.setError('sku', { type: 'server', message: error.message })
      } else if (error.code === 'BARCODE_TAKEN') {
        form.setError('barcode', { type: 'server', message: error.message })
      } else if (!applyAllFieldErrors(error, form.setError)) {
        form.setError('root', { message: error.message })
      }
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{variant ? `Edit ${variant.sku}` : 'Add variant'}</DialogTitle>
          <DialogDescription>Each variant has its own SKU, barcode and price, and is stocked separately.</DialogDescription>
        </DialogHeader>
        <form id="variant-form" noValidate onSubmit={onSubmit} className="grid gap-4">
          {form.formState.errors.root?.message && <FormAlert tone="error">{form.formState.errors.root.message}</FormAlert>}
          <VariantFields register={form.register} errors={form.formState.errors} showCost={showCost} />
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="variant-form" disabled={pending}>
            {pending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            {variant ? 'Save variant' : 'Add variant'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
