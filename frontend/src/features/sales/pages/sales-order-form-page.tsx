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
import type { SalesOrderDetailDto } from '@/lib/api/types'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { useSalesOrder, useSaveSalesOrder } from '../api'
import { CustomerSelect } from '../components/sales-parts'

const today = () => new Date().toISOString().slice(0, 10)

const schema = z.object({
  customerId: z.string().min(1, 'Choose a customer.'),
  warehouseId: z.string().min(1, 'Choose where it ships from.'),
  orderDate: z.string().min(1, 'Enter the order date.'),
  notes: z.string().trim().max(500).optional(),
  lines: z
    .array(
      z.object({
        variantId: z.string(),
        label: z.string(),
        sku: z.string(),
        unit: z.string(),
        /** Available in the chosen warehouse when the line was added (a hint; the server re-checks on confirm). */
        available: z.number().nullable(),
        quantity: z.number({ error: 'Enter a quantity.' }).gt(0, 'Must be more than 0.'),
        unitPrice: z.number({ error: 'Enter the price.' }).min(0, "Can't be negative."),
      }),
    )
    .min(1, 'Add at least one item.'),
})
type Values = z.infer<typeof schema>

/** Create a draft (no :id) or edit an existing DRAFT. Confirmed orders are read-only: their stock is reserved. */
export function SalesOrderFormPage() {
  const { id } = useParams()
  const existing = useSalesOrder(id ?? '', !!id)

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
          <Link to={`/sales/${id}`}>View order</Link>
        </Button>
      </Card>
    )
  }
  return <SalesOrderForm existing={existing.data} />
}

function SalesOrderForm({ existing }: { existing?: SalesOrderDetailDto }) {
  const navigate = useNavigate()
  const currency = useCurrency()
  const save = useSaveSalesOrder()
  const [picking, setPicking] = useState(false)

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { customerId: '', warehouseId: '', orderDate: today(), notes: '', lines: [] },
  })
  const lines = useFieldArray({ control: form.control, name: 'lines' })
  const warehouseId = useWatch({ control: form.control, name: 'warehouseId' })
  const watchedLines = useWatch({ control: form.control, name: 'lines' })
  const { errors } = form.formState

  useEffect(() => {
    if (existing) {
      form.reset({
        customerId: existing.customerId,
        warehouseId: existing.warehouseId,
        orderDate: existing.orderDate,
        notes: existing.notes ?? '',
        lines: existing.items.map((i) => ({
          variantId: i.variantId,
          label: i.variantName ? `${i.productName} (${i.variantName})` : i.productName,
          sku: i.sku,
          unit: i.unitOfMeasure,
          available: i.available,
          quantity: i.quantityOrdered,
          unitPrice: i.unitPrice,
        })),
      })
    }
  }, [existing, form])

  // Availability was looked up for the old warehouse: once it changes, the hint no longer applies.
  const changeWarehouse = (next: string) => {
    form.setValue('warehouseId', next, { shouldValidate: form.formState.isSubmitted })
    form.getValues('lines').forEach((_, i) => form.setValue(`lines.${i}.available`, null))
  }

  const total = (watchedLines ?? []).reduce(
    (sum, l) => sum + (Number.isFinite(l.quantity) ? l.quantity : 0) * (Number.isFinite(l.unitPrice) ? l.unitPrice : 0),
    0,
  )

  const onSubmit = form.handleSubmit(async (v) => {
    try {
      const id = await save.mutateAsync({
        id: existing?.id,
        body: {
          customerId: v.customerId,
          warehouseId: v.warehouseId,
          orderDate: v.orderDate,
          notes: v.notes || null,
          version: existing?.version ?? null,
          lines: v.lines.map((l) => ({ variantId: l.variantId, quantity: l.quantity, unitPrice: l.unitPrice })),
        },
      })
      toast.success(existing ? `${existing.number} saved.` : 'Draft sales order created.')
      navigate(`/sales/${id}`)
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
      <PageHeader
        title={existing ? `Edit ${existing.number}` : 'New sales order'}
        description="Saved as a draft. Confirm it to reserve the stock for this customer."
      />
      <form noValidate onSubmit={onSubmit} className="grid max-w-4xl gap-6">
        {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}

        <Card className="shadow-card">
          <CardContent className="grid gap-5 sm:grid-cols-2">
            <Controller
              control={form.control}
              name="customerId"
              render={({ field }) => (
                <div className="grid gap-1.5">
                  <CustomerSelect id="customer" label="Customer" value={field.value || undefined} onChange={field.onChange} invalid={!!errors.customerId} />
                  {errors.customerId && <p className="text-sm text-destructive">{errors.customerId.message}</p>}
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
                    label="Ship from"
                    value={field.value || undefined}
                    onChange={(v) => changeWarehouse(v ?? '')}
                    invalid={!!errors.warehouseId}
                  />
                  {errors.warehouseId && <p className="text-sm text-destructive">{errors.warehouseId.message}</p>}
                </div>
              )}
            />
            <FormField label="Order date" type="date" registration={form.register('orderDate')} error={errors.orderDate} />
            <div className="grid gap-1.5 sm:col-span-2">
              <Label htmlFor="so-notes">Notes</Label>
              <Textarea id="so-notes" rows={2} className="bg-card" placeholder="Optional, e.g. delivery instructions" {...form.register('notes')} />
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader className="flex flex-row items-center justify-between gap-3">
            <div>
              <CardTitle>Items</CardTitle>
              <CardDescription className="mt-1">
                Price defaults to the product's selling price. Choose the warehouse first to see what's available.
              </CardDescription>
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
              lines.fields.map((field, index) => {
                const qty = watchedLines?.[index]?.quantity
                const available = watchedLines?.[index]?.available ?? null
                const short = available !== null && qty !== undefined && Number.isFinite(qty) && qty > available
                return (
                  <div key={field.id} className="grid items-start gap-3 rounded-lg border p-3 sm:grid-cols-[minmax(0,1fr)_7rem_8rem_auto] sm:items-center">
                    <div className="min-w-0">
                      <p className="truncate font-medium">{field.label}</p>
                      <p className={short ? 'text-xs font-medium text-warning' : 'text-xs text-muted-foreground'}>
                        {field.sku}
                        {available !== null && ` · ${formatQuantity(available)} ${field.unit} available`}
                        {short && ' (not enough to confirm)'}
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
                        {...form.register(`lines.${index}.quantity`, { valueAsNumber: true })}
                      />
                      {errors.lines?.[index]?.quantity && <p className="mt-1 text-xs text-destructive">{errors.lines[index].quantity.message}</p>}
                    </div>
                    <div>
                      <Input
                        type="number"
                        step="0.01"
                        inputMode="decimal"
                        aria-label={`Unit price of ${field.label}`}
                        className="h-10 bg-card text-right"
                        aria-invalid={!!errors.lines?.[index]?.unitPrice || undefined}
                        {...form.register(`lines.${index}.unitPrice`, { valueAsNumber: true })}
                      />
                      {errors.lines?.[index]?.unitPrice && <p className="mt-1 text-xs text-destructive">{errors.lines[index].unitPrice.message}</p>}
                    </div>
                    <Button type="button" variant="ghost" size="icon" aria-label={`Remove ${field.label}`} onClick={() => lines.remove(index)}>
                      <Trash2 />
                    </Button>
                  </div>
                )
              })
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
            <Link to={existing ? `/sales/${existing.id}` : '/sales'}>Cancel</Link>
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
            available: warehouseId ? o.available : null,
            quantity: Number.NaN,
            unitPrice: o.sellingPrice,
          })
        }
      />
    </>
  )
}
