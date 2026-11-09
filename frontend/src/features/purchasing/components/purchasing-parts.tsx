import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle } from 'lucide-react'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { FormAlert } from '@/features/auth/components/form-alert'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import type { PurchaseOrderStatus, SupplierDto } from '@/lib/api/types'
import { cn } from '@/lib/utils'
import { PO_STATUS, useSaveSupplier, useSuppliers } from '../api'

export function PoStatusBadge({ status }: { status: PurchaseOrderStatus }) {
  const s = PO_STATUS[status]
  return (
    <Badge variant="outline" className={s.className}>
      {s.label}
    </Badge>
  )
}

/** Done ÷ ordered as a thin bar: partial deliveries (received or shipped) are visible at a glance in a list. */
export function ReceiptProgress({ received, ordered, label = 'Received' }: { received: number; ordered: number; label?: string }) {
  const pct = ordered > 0 ? Math.min(100, Math.round((received / ordered) * 100)) : 0
  return (
    <div className="flex items-center gap-2" title={`${pct}% ${label.toLowerCase()}`}>
      <div
        className="h-1.5 w-16 overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={label}
      >
        <div className={cn('h-full rounded-full', pct === 100 ? 'bg-success' : 'bg-primary')} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs text-muted-foreground tabular">{pct}%</span>
    </div>
  )
}

export function SupplierSelect({
  id,
  label,
  value,
  onChange,
  invalid,
}: {
  id?: string
  label?: string
  value: string | undefined
  onChange: (v: string) => void
  invalid?: boolean
}) {
  const { data } = useSuppliers({
    page: 1,
    pageSize: 100,
    isActive: true,
    sort: 'name:asc',
  })
  return (
    <div className="grid gap-1.5">
      {label && <Label htmlFor={id}>{label}</Label>}
      <Select value={value ?? ''} onValueChange={onChange}>
        <SelectTrigger id={id} aria-invalid={invalid || undefined} className="h-10 w-full bg-card">
          <SelectValue placeholder="Choose a supplier" />
        </SelectTrigger>
        <SelectContent>
          {(data?.items ?? []).map((s) => (
            <SelectItem key={s.id} value={s.id}>
              {s.name} <span className="text-muted-foreground">({s.code})</span>
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}

const supplierSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Enter a short code.')
    .max(20)
    .regex(/^[A-Za-z0-9-]+$/, 'Letters, numbers and - only.'),
  name: z.string().trim().min(1, 'Enter the supplier name.').max(150),
  contactName: z.string().trim().max(100).optional(),
  email: z.union([z.literal(''), z.string().trim().email('Enter a valid email address.')]).optional(),
  phone: z.string().trim().max(40).optional(),
  address: z.string().trim().max(300).optional(),
  paymentTermsDays: z.number({ error: 'Enter a number of days (0 for cash).' }).int().min(0).max(365),
  notes: z.string().trim().max(1000).optional(),
})
type SupplierValues = z.infer<typeof supplierSchema>

export function SupplierDialog({ supplier, open, onOpenChange }: { supplier?: SupplierDto; open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const save = useSaveSupplier()
  const form = useForm<SupplierValues>({
    resolver: zodResolver(supplierSchema),
  })

  useEffect(() => {
    if (open) {
      form.reset({
        code: supplier?.code ?? '',
        name: supplier?.name ?? '',
        contactName: supplier?.contactName ?? '',
        email: supplier?.email ?? '',
        phone: supplier?.phone ?? '',
        address: supplier?.address ?? '',
        paymentTermsDays: supplier?.paymentTermsDays ?? 30,
        notes: supplier?.notes ?? '',
      })
    }
  }, [open, supplier, form])

  const onSubmit = form.handleSubmit(async (v) => {
    const blank = (s?: string) => (s && s.trim() ? s.trim() : null)
    try {
      const id = await save.mutateAsync({
        id: supplier?.id,
        body: {
          code: v.code,
          name: v.name,
          contactName: blank(v.contactName),
          email: blank(v.email),
          phone: blank(v.phone),
          address: blank(v.address),
          paymentTermsDays: v.paymentTermsDays,
          notes: blank(v.notes),
        },
      })
      toast.success(supplier ? 'Supplier saved.' : `${v.name} added.`)
      onOpenChange(false)
      if (!supplier) navigate(`/suppliers/${id}`)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'SUPPLIER_CODE_TAKEN') form.setError('code', { type: 'server', message: error.message })
      else if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const { errors } = form.formState
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{supplier ? `Edit ${supplier.name}` : 'Add supplier'}</DialogTitle>
          <DialogDescription>Who you buy from, and how to reach them.</DialogDescription>
        </DialogHeader>
        <form id="supplier-form" noValidate onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-[9rem_1fr]">
          {errors.root?.message && (
            <div className="sm:col-span-2">
              <FormAlert tone="error">{errors.root.message}</FormAlert>
            </div>
          )}
          <FormField label="Code" autoCapitalize="characters" registration={form.register('code')} error={errors.code} />
          <FormField label="Name" registration={form.register('name')} error={errors.name} />
          <FormField label="Contact person" className="sm:col-span-2" registration={form.register('contactName')} error={errors.contactName} />
          <FormField label="Email" type="email" registration={form.register('email')} error={errors.email} className="sm:col-span-2" />
          <FormField label="Phone" type="tel" registration={form.register('phone')} error={errors.phone} />
          <FormField
            label="Payment terms (days)"
            type="number"
            inputMode="numeric"
            registration={form.register('paymentTermsDays', {
              valueAsNumber: true,
            })}
            error={errors.paymentTermsDays}
          />
          <FormField label="Address" className="sm:col-span-2" registration={form.register('address')} error={errors.address} />
          <div className="grid gap-1.5 sm:col-span-2">
            <Label htmlFor="supplier-notes">Notes</Label>
            <Textarea id="supplier-notes" rows={2} className="bg-card" {...form.register('notes')} />
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="supplier-form" disabled={save.isPending}>
            {save.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            {supplier ? 'Save supplier' : 'Add supplier'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
