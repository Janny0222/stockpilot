import { Package } from 'lucide-react'
import type { FieldErrors, UseFormRegister } from 'react-hook-form'
import { FormField } from '@/components/shared/form-field'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { CategoryDto } from '@/lib/api/types'
import { cn } from '@/lib/utils'
import { useCategories, useProductImage } from '../api'

export function ProductImage({
  productId,
  imageId,
  alt,
  className,
}: {
  productId: string
  imageId: string | null | undefined
  alt: string
  className?: string
}) {
  const { data: url } = useProductImage(productId, imageId)

  return (
    <span className={cn('grid shrink-0 place-items-center overflow-hidden rounded-lg border bg-accent', className)}>
      {url ? (
        <img src={url} alt={alt} className="size-full object-cover" />
      ) : (
        <Package className="size-1/2 max-h-6 max-w-6 text-muted-foreground" aria-hidden="true" />
      )}
    </span>
  )
}

const NONE = '__none'

/** Categories as an indented list: parents first, each followed by its children. */
export function sortCategoriesAsTree(categories: CategoryDto[]) {
  const parents = categories.filter((c) => !c.parentId).sort((a, b) => a.name.localeCompare(b.name))
  return parents.flatMap((p) => [
    { ...p, depth: 0 },
    ...categories
      .filter((c) => c.parentId === p.id)
      .sort((a, b) => a.name.localeCompare(b.name))
      .map((c) => ({ ...c, depth: 1 })),
  ])
}

export function CategorySelect({
  id,
  value,
  onChange,
  placeholder = 'No category',
  allowNone = true,
  onlyTopLevel = false,
  excludeId,
  label,
  className,
}: {
  id?: string
  value: string | undefined
  onChange: (value: string | undefined) => void
  placeholder?: string
  allowNone?: boolean
  onlyTopLevel?: boolean
  excludeId?: string
  label?: string
  className?: string
}) {
  const { data = [] } = useCategories()
  const options = sortCategoriesAsTree(data).filter((c) => (!onlyTopLevel || c.depth === 0) && c.id !== excludeId)

  const select = (
    <Select value={value ?? NONE} onValueChange={(v) => onChange(v === NONE ? undefined : v)}>
      <SelectTrigger id={id} className={cn('h-10 w-full bg-card', className)} aria-label={label ? undefined : placeholder}>
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        {allowNone && <SelectItem value={NONE}>{placeholder}</SelectItem>}
        {options.map((c) => (
          <SelectItem key={c.id} value={c.id} className={c.depth ? 'pl-7' : 'font-medium'}>
            {c.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )

  return label ? (
    <div className="grid gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {select}
    </div>
  ) : (
    select
  )
}

type VariantFieldNames = 'sku' | 'name' | 'barcode' | 'costPrice' | 'sellingPrice' | 'reorderLevel'

/**
 * The six variant inputs. `prefix` lets the same fields serve the create form (variants.0.sku…)
 * and the edit dialog (sku…). Cost is hidden for roles that can't see costs.
 */
export function VariantFields({
  register,
  errors,
  prefix = '',
  showCost,
  compact = false,
}: {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  register: UseFormRegister<any>
  errors: Partial<FieldErrors<Record<VariantFieldNames, unknown>>> | undefined
  prefix?: string
  showCost: boolean
  compact?: boolean
}) {
  const name = (field: VariantFieldNames) => `${prefix}${field}`
  return (
    <div className={cn('grid gap-4', compact ? 'sm:grid-cols-3' : 'sm:grid-cols-2')}>
      <FormField label="SKU" registration={register(name('sku'))} error={errors?.sku as never} placeholder="e.g. LED-E27-9W" autoCapitalize="characters" />
      <FormField label="Variant name" registration={register(name('name'))} error={errors?.name as never} placeholder="e.g. Red, 150 m (optional)" />
      <FormField label="Barcode" registration={register(name('barcode'))} error={errors?.barcode as never} placeholder="Optional" inputMode="numeric" />
      {showCost && (
        <FormField
          label="Cost price"
          type="number"
          step="0.01"
          inputMode="decimal"
          registration={register(name('costPrice'), { valueAsNumber: true })}
          error={errors?.costPrice as never}
        />
      )}
      <FormField
        label="Selling price"
        type="number"
        step="0.01"
        inputMode="decimal"
        registration={register(name('sellingPrice'), { valueAsNumber: true })}
        error={errors?.sellingPrice as never}
      />
      <FormField
        label="Reorder level"
        type="number"
        step="1"
        inputMode="decimal"
        registration={register(name('reorderLevel'), { valueAsNumber: true })}
        error={errors?.reorderLevel as never}
        hint="Low-stock alert at or below this quantity."
      />
    </div>
  )
}
