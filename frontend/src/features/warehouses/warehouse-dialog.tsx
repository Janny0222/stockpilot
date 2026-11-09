import { zodResolver } from '@hookform/resolvers/zod'
import { LoaderCircle } from 'lucide-react'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { z } from 'zod'
import { FormField } from '@/components/shared/form-field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FormAlert } from '@/features/auth/components/form-alert'
import { applyAllFieldErrors, toApiError } from '@/lib/api/errors'
import type { WarehouseDto } from '@/lib/api/types'
import { useSaveWarehouse } from './api'

const schema = z.object({
  code: z.string().trim().min(1, 'Enter a short code.').max(20).regex(/^[A-Za-z0-9-]+$/, 'Letters, numbers and - only.'),
  name: z.string().trim().min(1, 'Enter a name.').max(120),
  addressLine: z.string().trim().max(200).optional(),
  city: z.string().trim().max(100).optional(),
  country: z.string().trim().max(100).optional(),
})
type Values = z.infer<typeof schema>

export function WarehouseDialog({ warehouse, open, onOpenChange }: { warehouse?: WarehouseDto; open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  const save = useSaveWarehouse()
  const form = useForm<Values>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (open) {
      form.reset({
        code: warehouse?.code ?? '',
        name: warehouse?.name ?? '',
        addressLine: warehouse?.addressLine ?? '',
        city: warehouse?.city ?? '',
        country: warehouse?.country ?? '',
      })
    }
  }, [open, warehouse, form])

  const onSubmit = form.handleSubmit(async (v) => {
    try {
      const saved = await save.mutateAsync({
        id: warehouse?.id,
        body: { code: v.code, name: v.name, addressLine: v.addressLine || null, city: v.city || null, country: v.country || null },
      })
      toast.success(warehouse ? 'Warehouse saved.' : `${saved.name} added.`)
      onOpenChange(false)
      if (!warehouse) navigate(`/warehouses/${saved.id}`)
    } catch (e) {
      const error = toApiError(e)
      if (error.code === 'WAREHOUSE_CODE_TAKEN') form.setError('code', { type: 'server', message: error.message })
      else if (!applyAllFieldErrors(error, form.setError)) form.setError('root', { message: error.message })
    }
  })

  const { errors } = form.formState
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{warehouse ? `Edit ${warehouse.name}` : 'Add warehouse'}</DialogTitle>
          <DialogDescription>The code is a short label used in tables and transfers, e.g. MNL.</DialogDescription>
        </DialogHeader>
        <form id="warehouse-form" noValidate onSubmit={onSubmit} className="grid gap-4 sm:grid-cols-[8rem_1fr]">
          {errors.root?.message && <div className="sm:col-span-2"><FormAlert tone="error">{errors.root.message}</FormAlert></div>}
          <FormField label="Code" autoCapitalize="characters" registration={form.register('code')} error={errors.code} />
          <FormField label="Name" registration={form.register('name')} error={errors.name} />
          <FormField label="Address" className="sm:col-span-2" registration={form.register('addressLine')} error={errors.addressLine} placeholder="Optional" />
          <FormField label="City" registration={form.register('city')} error={errors.city} />
          <FormField label="Country" registration={form.register('country')} error={errors.country} />
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button type="submit" form="warehouse-form" disabled={save.isPending}>
            {save.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
            {warehouse ? 'Save warehouse' : 'Add warehouse'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
