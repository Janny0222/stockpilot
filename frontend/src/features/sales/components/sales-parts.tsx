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
import type { CustomerDto, SalesOrderStatus } from '@/lib/api/types'
import { SO_STATUS, useCustomers, useSaveCustomer } from '../api'

export function SoStatusBadge({ status }: { status: SalesOrderStatus }) {
  const s = SO_STATUS[status]
  return (
    <Badge variant="outline" className={s.className}>
      {s.label}
    </Badge>
  )
}

export function CustomerSelect({
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
  const { data } = useCustomers({ page: 1, pageSize: 100, isActive: true, sort: 'name:asc' })
  return (
    <div className="grid gap-1.5">
      {label && <Label htmlFor={id}>{label}</Label>}
      <Select value={value ?? ''} onValueChange={onChange}>
        <SelectTrigger id={id} aria-invalid={invalid || undefined} className="h-10 w-full bg-card">
          <SelectValue placeholder="Choose a customer" />
        </SelectTrigger>
        <SelectContent>
          {(data?.items ?? []).map((c) => (
            <SelectItem key={c.id} value={c.id}>
              {c.name} <span className="text-muted-foreground">({c.code})</span>
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  )
}

const customerSchema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Enter a short code.')
    .max(20)
    .regex(/^[A-Za-z0-9-]+$/, 'Letters, numbers and - only.'),
  name: z.string().trim().min(1, 'Enter the customer name.').max(150),
  contactName: z.string().trim().max(100).optional(),
  email: z.union([z.literal(''), z.string().trim().email('Enter a valid email address.')]).optional(),
  phone: z.string().trim().max(40).optional(),
  address: z.string().trim().max(300).optional(),
  paymentTermsDays: z.number({ error: 'Enter a number of days (0 for cash).' }).int().min(0).max(365),
  notes: z.string().trim().max(1000).optional(),
})
type CustomerValues = z.infer<typeof customerSchema>

export function CustomerDialog({ customer, open, onOpenChange }: { customer?: CustomerDto; open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const save = useSaveCustomer()
  const form = useForm<CustomerValues>({ resolver: zodResolver(customerSchema) })

  useEffect(() => {
    if (open) {
      form.reset({
        code: customer?.code ?? '',
        name: customer?.name ?? '',
        contactName: customer?.contactName ?? '',
        email: customer?.email ?? '',
        phone: customer?.phone ?? '',
        address: customer?.address ?? '',
        paymentTermsDays: customer?.paymentTermsDays ?? 0,
        notes: customer?.notes ?? '',
      })
    }
  }, [open, customer, form])

  const onSubmit = form.handleSubmit(async (v) => {
    const blank = (s?: string) => (s && s.trim() ? s.trim() : null)
    try {
      const id = await save.mutateAsync({
        id: customer?.id,
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
      toast.success(customer ? 'Customer saved.' : `${v.name} added.`)
      onOpenChange(false)
      if (!customer) navigate(`/customers/${id}`)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'CUSTOMER_CODE_TAKEN') form.setError('code', { type: 'server', message: error.message })
      else if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const { errors } = form.formState
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{customer ? `Edit ${customer.name}` : 'Add customer'}</DialogTitle>
          <DialogDescription>Who you sell to, and how to reach them.</DialogDescription>
        </DialogHeader>
        <form id="customer-form" noValidate onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-[9rem_1fr]">
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
            registration={form.register('paymentTermsDays', { valueAsNumber: true })}
            error={errors.paymentTermsDays}
          />
          <FormField label="Address" className="sm:col-span-2" registration={form.register('address')} error={errors.address} />
          <div className="grid gap-1.5 sm:col-span-2">
            <Label htmlFor="customer-notes">Notes</Label>
            <Textarea id="customer-notes" rows={2} className="bg-card" {...form.register('notes')} />
          </div>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="customer-form" disabled={save.isPending}>
            {save.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            {customer ? 'Save customer' : 'Add customer'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
