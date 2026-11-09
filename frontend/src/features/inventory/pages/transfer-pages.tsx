import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, ArrowRight, LoaderCircle, PackagePlus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { FormAlert } from '@/features/auth/components/form-alert'
import { useIdempotencyKey } from '@/hooks/use-idempotency-key'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import { formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { useTransfer, useTransferStock } from '../api'
import { ItemPickerDialog, WarehouseSelect } from '../components/inventory-parts'

const schema = z
  .object({
    sourceWarehouseId: z.string().min(1, 'Choose where the stock comes from.'),
    destinationWarehouseId: z.string().min(1, 'Choose where the stock goes.'),
    note: z.string().trim().max(300).optional(),
    lines: z
      .array(
        z.object({
          variantId: z.string(),
          label: z.string(),
          sku: z.string(),
          unit: z.string(),
          available: z.number(),
          quantity: z.number({ error: 'Enter a quantity.' }).gt(0, 'Must be more than 0.'),
        }),
      )
      .min(1, 'Add at least one item.'),
  })
  .refine((v) => v.sourceWarehouseId !== v.destinationWarehouseId, { path: ['destinationWarehouseId'], message: 'Choose a different warehouse.' })
type Values = z.infer<typeof schema>

export function NewTransferPage() {
  const navigate = useNavigate()
  const transfer = useTransferStock()
  const { key, renew } = useIdempotencyKey()
  const [picking, setPicking] = useState(false)
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { sourceWarehouseId: '', destinationWarehouseId: '', note: '', lines: [] } })
  const lines = useFieldArray({ control: form.control, name: 'lines' })
  const source = useWatch({ control: form.control, name: 'sourceWarehouseId' })
  const destination = useWatch({ control: form.control, name: 'destinationWarehouseId' })
  const watchedLines = useWatch({ control: form.control, name: 'lines' })
  const { errors } = form.formState

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      const result = await transfer.mutateAsync({
        idempotencyKey: key,
        body: {
          sourceWarehouseId: values.sourceWarehouseId,
          destinationWarehouseId: values.destinationWarehouseId,
          note: values.note || null,
          lines: values.lines.map((l) => ({ variantId: l.variantId, quantity: l.quantity })),
        },
      })
      renew()
      toast.success(`Transfer ${result.documentNumber} completed.`)
      navigate(`/inventory/transfers/${result.correlationId}`)
    } catch (e) {
      const error = toApiError(e)
      if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  return (
    <>
      <PageHeader title="Transfer stock" description="Moves stock between two warehouses in one step: it leaves the source and arrives at the destination together." />

      <form noValidate onSubmit={onSubmit} className="grid max-w-4xl gap-6">
        {errors.root?.message && <FormAlert tone="error">{errors.root.message}</FormAlert>}

        <Card className="shadow-card">
          <CardContent className="grid items-end gap-5 sm:grid-cols-[1fr_auto_1fr]">
            <Controller
              control={form.control}
              name="sourceWarehouseId"
              render={({ field }) => (
                <div className="grid gap-1.5">
                  <WarehouseSelect
                    id="source"
                    label="From"
                    value={field.value || undefined}
                    exclude={destination}
                    invalid={!!errors.sourceWarehouseId}
                    onChange={(v) => {
                      field.onChange(v ?? '')
                      lines.replace([]) // availability shown per line belongs to the old source
                    }}
                  />
                  {errors.sourceWarehouseId && <p className="text-sm text-destructive">{errors.sourceWarehouseId.message}</p>}
                </div>
              )}
            />
            <ArrowRight className="mb-3 hidden size-5 text-muted-foreground sm:block" aria-hidden="true" />
            <Controller
              control={form.control}
              name="destinationWarehouseId"
              render={({ field }) => (
                <div className="grid gap-1.5">
                  <WarehouseSelect id="destination" label="To" value={field.value || undefined} exclude={source} invalid={!!errors.destinationWarehouseId} onChange={(v) => field.onChange(v ?? '')} />
                  {errors.destinationWarehouseId && <p className="text-sm text-destructive">{errors.destinationWarehouseId.message}</p>}
                </div>
              )}
            />
            <FormField label="Note" className="sm:col-span-3" registration={form.register('note')} error={errors.note} placeholder="Optional, e.g. restock for weekend sale" />
          </CardContent>
        </Card>

        <Card className="shadow-card">
          <CardHeader className="flex flex-row items-center justify-between gap-3">
            <div>
              <CardTitle>Items</CardTitle>
              <CardDescription className="mt-1">{source ? 'Availability shown is at the source warehouse.' : 'Choose the source warehouse first.'}</CardDescription>
            </div>
            <Button type="button" variant="outline" onClick={() => setPicking(true)} disabled={!source}>
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
                const over = line && line.quantity > line.available
                return (
                  <div key={field.id} className="grid items-start gap-3 rounded-lg border p-3 sm:grid-cols-[minmax(0,1fr)_8rem_auto] sm:items-center">
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
                    <Button type="button" variant="ghost" size="icon" aria-label={`Remove ${field.label}`} onClick={() => lines.remove(index)}>
                      <Trash2 />
                    </Button>
                  </div>
                )
              })
            )}
          </CardContent>
        </Card>

        <div className="flex flex-wrap gap-3">
          <Button type="submit" size="lg" disabled={transfer.isPending}>
            {transfer.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Complete transfer
          </Button>
          <Button asChild type="button" variant="outline" size="lg">
            <Link to="/inventory?tab=transfers">Cancel</Link>
          </Button>
        </div>
      </form>

      <ItemPickerDialog
        open={picking}
        onOpenChange={setPicking}
        warehouseId={source}
        excludeIds={(watchedLines ?? []).map((l) => l.variantId)}
        onPick={(o) =>
          lines.append({
            variantId: o.variantId,
            label: o.variantName ? `${o.productName} (${o.variantName})` : o.productName,
            sku: o.sku,
            unit: o.unitOfMeasure,
            available: o.available,
            quantity: Number.NaN,
          })
        }
      />
    </>
  )
}

const dateTime = new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeStyle: 'short' })

export function TransferDetailPage() {
  const { id = '' } = useParams()
  const query = useTransfer(id)
  const currency = useCurrency()

  const back = (
    <Button asChild variant="outline">
      <Link to="/inventory?tab=transfers"><ArrowLeft /> All transfers</Link>
    </Button>
  )

  if (query.isPending) return <Skeleton className="h-64 max-w-3xl" />
  if (query.isError) {
    return (
      <>
        <PageHeader title="Transfer" actions={back} />
        <Card className="shadow-card"><ErrorState error={query.error} /></Card>
      </>
    )
  }

  const t = query.data
  const showCost = t.items.some((i) => i.unitCost !== null && i.unitCost !== undefined)

  return (
    <>
      <PageHeader
        title={`Transfer ${t.number}`}
        description={`${t.sourceWarehouseName} → ${t.destinationWarehouseName} · ${dateTime.format(new Date(t.completedAt))}${t.completedBy ? ` · by ${t.completedBy}` : ''}`}
        actions={back}
      />
      <Card className="max-w-4xl gap-0 py-0 shadow-card">
        <Table>
          <caption className="sr-only">Items in transfer {t.number}</caption>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead className="pl-6">Item</TableHead>
              <TableHead className="text-right">Quantity</TableHead>
              {showCost && <TableHead className="pr-6 text-right">Unit cost</TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {t.items.map((i) => (
              <TableRow key={i.variantId}>
                <TableCell className="pl-6">
                  <Link to={`/products/${i.productId}`} className="font-medium hover:underline">{i.productName}</Link>
                  <p className="text-xs text-muted-foreground">{i.sku}{i.variantName && ` · ${i.variantName}`}</p>
                </TableCell>
                <TableCell className="text-right tabular">{formatQuantity(i.quantity)} {i.unitOfMeasure}</TableCell>
                {showCost && <TableCell className="pr-6 text-right tabular">{formatMoney(i.unitCost, currency)}</TableCell>}
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {t.note && <p className="border-t px-6 py-4 text-sm text-muted-foreground">Note: {t.note}</p>}
      </Card>
    </>
  )
}
