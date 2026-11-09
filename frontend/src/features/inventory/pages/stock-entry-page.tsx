import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle, PackagePlus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { FormAlert } from '@/features/auth/components/form-alert'
import { useIdempotencyKey } from '@/hooks/use-idempotency-key'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useStockIn, useStockOut } from '../api'
import { ItemPickerDialog, WarehouseSelect } from '../components/inventory-parts'

const quantity = z
  .number({ error: 'Enter a quantity.' })
  .gt(0, 'Must be more than 0.')
  .refine((q) => Math.round(q * 1000) === q * 1000, 'Use at most 3 decimals.')

const schema = z.object({
  warehouseId: z.string({ error: 'Choose a warehouse.' }).min(1, 'Choose a warehouse.'),
  reference: z.string().trim().max(60).optional(),
  note: z.string().trim().max(300).optional(),
  lines: z
    .array(
      z.object({
        variantId: z.string(),
        label: z.string(),
        sku: z.string(),
        unit: z.string(),
        available: z.number(),
        quantity,
        unitCost: z.number().min(0, "Can't be negative.").optional(),
      }),
    )
    .min(1, 'Add at least one item.'),
})
type Values = z.infer<typeof schema>

/** One form for both directions: stock-in (receive, with unit cost) and stock-out (issue, shows availability). */
export function StockEntryPage({ mode }: { mode: 'in' | 'out' }) {
  const navigate = useNavigate()
  const currency = useCurrency()
  const canViewCost = useHasPermission(P.Inventory.ViewCost)
  const stockIn = useStockIn()
  const stockOut = useStockOut()
  const mutation = mode === 'in' ? stockIn : stockOut
  const { key, renew } = useIdempotencyKey()
  const [picking, setPicking] = useState(false)

  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { reference: '', note: '', lines: [] } })
  const lines = useFieldArray({ control: form.control, name: 'lines' })
  const warehouseId = useWatch({ control: form.control, name: 'warehouseId' })
  const watchedLines = useWatch({ control: form.control, name: 'lines' })
  const { errors } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const result = await mutation.mutateAsync({
        idempotencyKey: key,
        body: {
          warehouseId: values.warehouseId,
          reference: values.reference || null,
          note: values.note || null,
          lines: values.lines.map((l) => ({
            variantId: l.variantId,
            quantity: l.quantity,
            unitCost: mode === 'in' && canViewCost && Number.isFinite(l.unitCost) ? (l.unitCost ?? null) : null,
          })),
        },
      })
      renew()
      toast.success(`${mode === 'in' ? 'Stock in' : 'Stock out'} recorded: ${result.movements.length} item${result.movements.length === 1 ? '' : 's'}.`)
      navigate('/inventory?tab=movements')
    } catch (e) {
      const error = toApiError(e)
      if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const title = mode === 'in' ? 'Stock in' : 'Stock out'
  const total = mode === 'in' && canViewCost ? (watchedLines ?? []).reduce((sum, l) => sum + (l.quantity || 0) * (l.unitCost || 0), 0) : 0

  return (
    <>
      <PageHeader
        title={title}
        description={mode === 'in' ? 'Record stock arriving at a warehouse: deliveries, returns, opening balances.' : 'Record stock leaving a warehouse outside of a sales order: internal use, samples, write-offs.'}
      />

      <form noValidate onSubmit={onSubmit} className="grid max-w-4xl gap-6">
        {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}

        <Card className="shadow-card">
          <CardContent className="grid gap-5 sm:grid-cols-2">
            <Controller
              control={form.control}
              name="warehouseId"
              render={({ field }) => (
                <div className="grid gap-1.5">
                  <WarehouseSelect id="warehouse" label="Warehouse" value={field.value} onChange={(v) => field.onChange(v ?? '')} invalid={!!errors.warehouseId} />
                  {errors.warehouseId && <p className="text-sm text-destructive" role="alert">{errors.warehouseId.message}</p>}
                </div>
              )}
            />
            <FormField label="Reference" registration={form.register('reference')} error={errors.reference} placeholder={mode === 'in' ? 'e.g. DR-10293 (optional)' : 'e.g. Job order 55 (optional)'} />
            <FormField label="Note" className="sm:col-span-2" registration={form.register('note')} error={errors.note} placeholder="Optional" />
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader className="flex flex-row items-center justify-between gap-3">
            <div>
              <CardTitle>Items</CardTitle>
              <CardDescription className="mt-1">{warehouseId ? 'Add each item and its quantity.' : 'Choose a warehouse first to see what it holds.'}</CardDescription>
            </div>
            <Button type="button" variant="outline" onClick={() => setPicking(true)} disabled={!warehouseId}>
              <PackagePlus /> Add item
            </Button>
          </CardHeader>
          <CardContent className="grid gap-3">
            {errors.lines?.root?.message && <p className="text-sm text-destructive" role="alert">{errors.lines.root.message}</p>}
            {errors.lines?.message && <p className="text-sm text-destructive" role="alert">{errors.lines.message}</p>}
            {lines.fields.length === 0 ? (
              <p className="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground">No items yet.</p>
            ) : (
              lines.fields.map((field, index) => {
                const line = watchedLines?.[index]
                const over = mode === 'out' && line && line.quantity > line.available
                return (
                  <div key={field.id} className="grid items-start gap-3 rounded-lg border p-3 sm:grid-cols-[minmax(0,1fr)_8rem_auto] sm:items-center lg:grid-cols-[minmax(0,1fr)_8rem_9rem_auto]">
                    <div className="min-w-0">
                      <p className="truncate font-medium">{field.label}</p>
                      <p className="text-xs text-muted-foreground">
                        {field.sku} · {formatQuantity(field.available)} {field.unit} available
                      </p>
                    </div>
                    <div>
                      <Input
                        type="number"
                        step="any"
                        inputMode="decimal"
                        aria-label={`Quantity of ${field.label}`}
                        aria-invalid={!!errors.lines?.[index]?.quantity || over || undefined}
                        className="h-10 bg-card text-right"
                        {...form.register(`lines.${index}.quantity`, { valueAsNumber: true })}
                      />
                      {errors.lines?.[index]?.quantity && <p className="mt-1 text-xs text-destructive">{errors.lines[index].quantity.message}</p>}
                      {over && !errors.lines?.[index]?.quantity && <p className="mt-1 text-xs text-warning">More than available</p>}
                    </div>
                    {mode === 'in' && canViewCost && (
                      <Input
                        type="number"
                        step="0.01"
                        inputMode="decimal"
                        aria-label={`Unit cost of ${field.label}`}
                        className="h-10 bg-card text-right"
                        {...form.register(`lines.${index}.unitCost`, { valueAsNumber: true })}
                      />
                    )}
                    <Button type="button" variant="ghost" size="icon" aria-label={`Remove ${field.label}`} onClick={() => lines.remove(index)}>
                      <Trash2 />
                    </Button>
                  </div>
                )
              })
            )}
            {mode === 'in' && canViewCost && lines.fields.length > 0 && (
              <p className="text-right text-sm text-muted-foreground">
                Total cost <span className="ml-2 font-semibold text-foreground tabular">{formatMoney(total, currency)}</span>
              </p>
            )}
          </CardContent>
        </Card>

        <div className="flex flex-wrap gap-3">
          <Button type="submit" size="lg" disabled={mutation.isPending}>
            {mutation.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Record {title.toLowerCase()}
          </Button>
          <Button asChild type="button" variant="outline" size="lg">
            <Link to="/inventory">Cancel</Link>
          </Button>
        </div>
      </form>

      <ItemPickerDialog
        open={picking}
        onOpenChange={setPicking}
        warehouseId={warehouseId}
        excludeIds={(watchedLines ?? []).map((l) => l.variantId)}
        onPick={(o) =>
          lines.append({
            variantId: o.variantId,
            label: o.variantName ? `${o.productName} (${o.variantName})` : o.productName,
            sku: o.sku,
            unit: o.unitOfMeasure,
            available: o.available,
            quantity: Number.NaN,
            unitCost: o.costPrice ?? undefined,
          })
        }
      />
    </>
  )
}
