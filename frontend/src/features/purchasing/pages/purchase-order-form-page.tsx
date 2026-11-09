import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle, PackagePlus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { FormAlert } from '@/features/auth/components/form-alert'
import { ItemPickerDialog, WarehouseSelect } from '@/features/inventory/components/inventory-parts'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import type { PurchaseOrderDetailDto } from '@/lib/api/types'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { usePurchaseOrder, useSavePurchaseOrder } from '../api'
import { SupplierSelect } from '../components/purchasing-parts'

const today = () => new Date().toISOString().slice(0, 10)

const schema = z
  .object({
    supplierId: z.string().min(1, 'Choose a supplier.'),
    warehouseId: z.string().min(1, 'Choose where it will be delivered.'),
    orderDate: z.string().min(1, 'Enter the order date.'),
    expectedDate: z.string().optional(),
    notes: z.string().trim().max(500).optional(),
    lines: z
      .array(
        z.object({
          variantId: z.string(),
          label: z.string(),
          sku: z.string(),
          unit: z.string(),
          onHand: z.number(),
          quantity: z.number({ error: 'Enter a quantity.' }).gt(0, 'Must be more than 0.'),
          unitCost: z.number({ error: 'Enter the unit cost.' }).min(0, "Can't be negative."),
        }),
      )
      .min(1, 'Add at least one item.'),
  })
  .refine((v) => !v.expectedDate || v.expectedDate >= v.orderDate, {
    path: ['expectedDate'],
    message: "Can't be before the order date.",
  })
type Values = z.infer<typeof schema>

/** Create a draft (no :id) or edit an existing DRAFT. Non-draft orders are read-only by design. */
export function PurchaseOrderFormPage() {
  const { id } = useParams()
  const existing = usePurchaseOrder(id ?? '', !!id)

  if (id && existing.isPending) return <Skeleton className="h-96 max-w-4xl" />
  if (id && existing.isError)
    return (
      <Card className="shadow-card">
        <ErrorState error={existing.error} />
      </Card>
    )
  if (id && existing.data && existing.data.status !== 'Draft') {
    return (
      <Card className="max-w-xl p-6 shadow-card">
        <p>{existing.data.number} is no longer a draft, so it can't be edited.</p>
        <Button asChild className="mt-4 w-fit">
          <Link to={`/purchases/${id}`}>View order</Link>
        </Button>
      </Card>
    )
  }
  return <PurchaseOrderForm existing={existing.data} />
}

function PurchaseOrderForm({ existing }: { existing?: PurchaseOrderDetailDto }) {
  const navigate = useNavigate()
  const currency = useCurrency()
  const save = useSavePurchaseOrder()
  const [picking, setPicking] = useState(false)

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      supplierId: '',
      warehouseId: '',
      orderDate: today(),
      expectedDate: '',
      notes: '',
      lines: [],
    },
  })
  const lines = useFieldArray({ control: form.control, name: 'lines' })
  const warehouseId = useWatch({ control: form.control, name: 'warehouseId' })
  const watchedLines = useWatch({ control: form.control, name: 'lines' })
  const { errors } = form.formState

  useEffect(() => {
    if (existing) {
      form.reset({
        supplierId: existing.supplierId,
        warehouseId: existing.warehouseId,
        orderDate: existing.orderDate,
        expectedDate: existing.expectedDate ?? '',
        notes: existing.notes ?? '',
        lines: existing.items.map((i) => ({
          variantId: i.variantId,
          label: i.variantName ? `${i.productName} (${i.variantName})` : i.productName,
          sku: i.sku,
          unit: i.unitOfMeasure,
          onHand: 0,
          quantity: i.quantityOrdered,
          unitCost: i.unitCost ?? 0,
        })),
      })
    }
  }, [existing, form])

  const total = (watchedLines ?? []).reduce(
    (sum, l) => sum + (Number.isFinite(l.quantity) ? l.quantity : 0) * (Number.isFinite(l.unitCost) ? l.unitCost : 0),
    0,
  )

  const onSubmit = form.handleSubmit(async (v) => {
    try {
      const id = await save.mutateAsync({
        id: existing?.id,
        body: {
          supplierId: v.supplierId,
          warehouseId: v.warehouseId,
          orderDate: v.orderDate,
          expectedDate: v.expectedDate || null,
          notes: v.notes || null,
          version: existing?.version ?? null,
          lines: v.lines.map((l) => ({
            variantId: l.variantId,
            quantity: l.quantity,
            unitCost: l.unitCost,
          })),
        },
      })
      toast.success(existing ? `${existing.number} saved.` : 'Draft purchase order created.')
      navigate(`/purchases/${id}`)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'CONCURRENCY_CONFLICT') {
        toast.error('Someone else changed this order. Reload it and try again.')
      } else if (!applyAllFieldErrors(error, form.setError)) {
        form.setError('root', { message: error.message })
      }
    }
  })

  return (
    <>
      <PageHeader title={existing ? `Edit ${existing.number}` : 'New purchase order'} description="Saved as a draft. Submit it for approval when it's ready." />
      <form noValidate onSubmit={onSubmit} className="grid max-w-4xl gap-6">
        {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}

        <Card className="shadow-card">
          <CardContent className="grid gap-5 sm:grid-cols-2">
            <Controller
              control={form.control}
              name="supplierId"
              render={({ field }) => (
                <div className="grid gap-1.5">
                  <SupplierSelect id="supplier" label="Supplier" value={field.value || undefined} onChange={field.onChange} invalid={!!errors.supplierId} />
                  {errors.supplierId && <p className="text-sm text-destructive">{errors.supplierId.message}</p>}
                </div>
              )}
            />
            <Controller
              control={form.control}
              name="warehouseId"
              render={({ field }) => (
                <div className="grid gap-1.5">
                  <WarehouseSelect
                    id="warehouse"
                    label="Deliver to"
                    value={field.value || undefined}
                    onChange={(v) => field.onChange(v ?? '')}
                    invalid={!!errors.warehouseId}
                  />
                  {errors.warehouseId && <p className="text-sm text-destructive">{errors.warehouseId.message}</p>}
                </div>
              )}
            />
            <FormField label="Order date" type="date" registration={form.register('orderDate')} error={errors.orderDate} />
            <FormField label="Expected delivery" type="date" registration={form.register('expectedDate')} error={errors.expectedDate} />
            <div className="grid gap-1.5 sm:col-span-2">
              <Label htmlFor="po-notes">Notes for the supplier</Label>
              <Textarea id="po-notes" rows={2} className="bg-card" placeholder="Optional" {...form.register('notes')} />
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader className="flex flex-row items-center justify-between gap-3">
            <div>
              <CardTitle>Items</CardTitle>
              <CardDescription className="mt-1">Unit cost defaults to the product's cost price. Change it to the supplier's quote.</CardDescription>
            </div>
            <Button type="button" variant="outline" onClick={() => setPicking(true)}>
              <PackagePlus /> Add item
            </Button>
          </CardHeader>
          <CardContent className="grid gap-3">
            {(errors.lines?.root?.message || errors.lines?.message) && (
              <p className="text-sm text-destructive" role="alert">
                {errors.lines?.root?.message ?? errors.lines?.message}
              </p>
            )}
            {lines.fields.length === 0 ? (
              <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">No items yet.</p>
            ) : (
              lines.fields.map((field, index) => (
                <div key={field.id} className="grid items-start gap-3 rounded-lg border p-3 sm:grid-cols-[minmax(0,1fr)_7rem_8rem_auto] sm:items-center">
                  <div className="min-w-0">
                    <p className="truncate font-medium">{field.label}</p>
                    <p className="text-xs text-muted-foreground">
                      {field.sku}
                      {warehouseId && field.onHand > 0 && ` · ${formatQuantity(field.onHand)} ${field.unit} on hand`}
                    </p>
                  </div>
                  <div>
                    <Input
                      type="number"
                      step="any"
                      inputMode="decimal"
                      aria-label={`Quantity of ${field.label}`}
                      className="h-10 bg-card text-right"
                      aria-invalid={!!errors.lines?.[index]?.quantity || undefined}
                      {...form.register(`lines.${index}.quantity`, {
                        valueAsNumber: true,
                      })}
                    />
                    {errors.lines?.[index]?.quantity && <p className="mt-1 text-xs text-destructive">{errors.lines[index].quantity.message}</p>}
                  </div>
                  <div>
                    <Input
                      type="number"
                      step="0.01"
                      inputMode="decimal"
                      aria-label={`Unit cost of ${field.label}`}
                      className="h-10 bg-card text-right"
                      aria-invalid={!!errors.lines?.[index]?.unitCost || undefined}
                      {...form.register(`lines.${index}.unitCost`, {
                        valueAsNumber: true,
                      })}
                    />
                    {errors.lines?.[index]?.unitCost && <p className="mt-1 text-xs text-destructive">{errors.lines[index].unitCost.message}</p>}
                  </div>
                  <Button type="button" variant="ghost" size="icon" aria-label={`Remove ${field.label}`} onClick={() => lines.remove(index)}>
                    <Trash2 />
                  </Button>
                </div>
              ))
            )}
            {lines.fields.length > 0 && (
              <p className="text-right text-sm text-muted-foreground">
                Order total <span className="ml-2 text-base font-bold text-foreground tabular">{formatMoney(total, currency)}</span>
              </p>
            )}
          </CardContent>
        </Card>

        <div className="flex flex-wrap gap-3">
          <Button type="submit" size="lg" disabled={save.isPending}>
            {save.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            {existing ? 'Save draft' : 'Create draft'}
          </Button>
          <Button asChild type="button" variant="outline" size="lg">
            <Link to={existing ? `/purchases/${existing.id}` : '/purchases'}>Cancel</Link>
          </Button>
        </div>
      </form>

      <ItemPickerDialog
        open={picking}
        onOpenChange={setPicking}
        warehouseId={warehouseId || undefined}
        excludeIds={(watchedLines ?? []).map((l) => l.variantId)}
        onPick={(o) =>
          lines.append({
            variantId: o.variantId,
            label: o.variantName ? `${o.productName} (${o.variantName})` : o.productName,
            sku: o.sku,
            unit: o.unitOfMeasure,
            onHand: o.onHand,
            quantity: Number.NaN,
            unitCost: o.costPrice ?? 0,
          })
        }
      />
    </>
  )
}
