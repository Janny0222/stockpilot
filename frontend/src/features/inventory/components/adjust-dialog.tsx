import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle } from 'lucide-react'
import { useEffect } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { FormAlert } from '@/features/auth/components/form-alert'
import { useIdempotencyKey } from '@/hooks/use-idempotency-key'
import { toApiError } from '@/lib/api/errors'
import type { StockLevelDto } from '@/lib/api/types'
import { formatQuantity } from '@/lib/format'
import { ADJUSTMENT_REASONS, useAdjustStock } from '../api'
import { QuantityChange } from './inventory-parts'

const schema = z
  .object({
    countedQuantity: z.number({ error: 'Enter the counted quantity.' }).min(0, "Can't be negative."),
    reason: z.enum(['CountCorrection', 'Damaged', 'Lost', 'Found', 'Expired', 'Other']),
    note: z.string().trim().max(300).optional(),
  })
  .refine((v) => v.reason !== 'Other' || !!v.note, { path: ['note'], message: 'Explain the adjustment when the reason is Other.' })
type Values = z.infer<typeof schema>

/**
 * Stocktake-style adjustment: you enter what you COUNTED, the system works out the difference.
 * People count shelves; they don't count differences.
 */
export function AdjustDialog({ row, open, onOpenChange }: { row: StockLevelDto | null; open: boolean; onOpenChange: (open: boolean) => void }) {
  const adjust = useAdjustStock()
  const { key, renew } = useIdempotencyKey()
  const form = useForm<Values>({ resolver: zodResolver(schema) })
  const counted = useWatch({ control: form.control, name: 'countedQuantity' })

  useEffect(() => {
    if (open && row) form.reset({ countedQuantity: row.onHand, reason: 'CountCorrection', note: '' })
  }, [open, row, form])

  if (!row) return null
  const delta = Number.isFinite(counted) ? counted - row.onHand : 0

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await adjust.mutateAsync({
        idempotencyKey: key,
        body: {
          warehouseId: row.warehouseId,
          variantId: row.variantId,
          countedQuantity: values.countedQuantity,
          reason: values.reason,
          note: values.note || null,
        },
      })
      renew()
      toast.success(`${row.sku} adjusted to ${formatQuantity(values.countedQuantity)} ${row.unitOfMeasure}.`)
      onOpenChange(false)
    } catch (e) {
      form.setError('root', { message: toApiError(e).message })
    }
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Adjust stock</DialogTitle>
          <DialogDescription>
            {row.productName} · {row.sku} at {row.warehouseCode}. System count: {formatQuantity(row.onHand)} {row.unitOfMeasure}.
          </DialogDescription>
        </DialogHeader>
        <form id="adjust-form" noValidate onSubmit={onSubmit} className="grid gap-4">
          {form.formState.errors.root?.message && <FormAlert tone="error">{form.formState.errors.root.message}</FormAlert>}
          <FormField
            label="Counted quantity"
            type="number"
            step="any"
            inputMode="decimal"
            autoFocus
            registration={form.register('countedQuantity', { valueAsNumber: true })}
            error={form.formState.errors.countedQuantity}
            hint={delta !== 0 ? <span>Change: <QuantityChange value={delta} /> {row.unitOfMeasure}</span> : 'Matches the system count.'}
          />
          <div className="grid gap-1.5">
            <Label htmlFor="adjust-reason">Reason</Label>
            <Controller
              control={form.control}
              name="reason"
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="adjust-reason" className="h-10 w-full bg-card">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {ADJUSTMENT_REASONS.map((r) => (
                      <SelectItem key={r.value} value={r.value}>
                        {r.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          <FormField label="Note" registration={form.register('note')} error={form.formState.errors.note} placeholder="Optional, e.g. found behind rack B3" />
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="adjust-form" disabled={adjust.isPending || delta === 0}>
            {adjust.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            Save adjustment
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
