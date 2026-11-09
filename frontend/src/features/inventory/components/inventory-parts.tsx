import { Search } from 'lucide-react'
import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { useActiveWarehouses } from '@/features/warehouses/api'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import type { InventoryMovementType, StockStatus, VariantOptionDto } from '@/lib/api/types'
import { formatQuantity } from '@/lib/format'
import { cn } from '@/lib/utils'
import { MOVEMENT_LABELS, useVariantOptions } from '../api'

const ALL = '__all'

export function WarehouseSelect({
  id,
  label,
  value,
  onChange,
  allowAll = false,
  exclude,
  className,
  invalid,
}: {
  id?: string
  label?: string
  value: string | undefined
  onChange: (value: string | undefined) => void
  allowAll?: boolean
  exclude?: string
  className?: string
  invalid?: boolean
}) {
  const { data } = useActiveWarehouses()
  const options = (data?.items ?? []).filter((w) => w.id !== exclude)

  const select = (
    <Select value={value ?? (allowAll ? ALL : '')} onValueChange={(v) => onChange(v === ALL ? undefined : v)}>
      <SelectTrigger id={id} aria-invalid={invalid || undefined} className={cn('h-10 w-full bg-card', className)} aria-label={label ? undefined : 'Warehouse'}>
        <SelectValue placeholder="Choose a warehouse" />
      </SelectTrigger>
      <SelectContent>
        {allowAll && <SelectItem value={ALL}>All warehouses</SelectItem>}
        {options.map((w) => (
          <SelectItem key={w.id} value={w.id}>
            {w.name} <span className="text-muted-foreground">({w.code})</span>
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

/**
 * "Add item" picker: search by name, SKU or scanned barcode, see what's available in the selected warehouse,
 * click to add. A dialog (rather than a dropdown) stays usable on phones and with barcode scanners.
 */
export function ItemPickerDialog({
  open,
  onOpenChange,
  warehouseId,
  excludeIds,
  onPick,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  warehouseId: string | undefined
  excludeIds: string[]
  onPick: (option: VariantOptionDto) => void
}) {
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search, 250)
  const query = useVariantOptions(debounced, warehouseId, open)
  const options = (query.data ?? []).filter((o) => !excludeIds.includes(o.variantId))

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        if (!next) setSearch('')
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Add item</DialogTitle>
          <DialogDescription>Search by product name, SKU, or scan a barcode.</DialogDescription>
        </DialogHeader>
        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input autoFocus value={search} onChange={(e) => setSearch(e.target.value)} placeholder="e.g. THHN or LED-E27" className="h-10 pl-9" aria-label="Search items" />
        </div>
        <div className="max-h-80 overflow-y-auto rounded-lg border">
          {query.isPending ? (
            <div className="grid gap-2 p-3">
              <Skeleton className="h-10" />
              <Skeleton className="h-10" />
            </div>
          ) : options.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{debounced ? `No items match "${debounced}".` : 'No items to add.'}</p>
          ) : (
            <ul role="listbox" aria-label="Matching items" className="divide-y">
              {options.map((o) => (
                <li key={o.variantId} role="option" aria-selected={false}>
                  <button
                    type="button"
                    onClick={() => {
                      onPick(o)
                      onOpenChange(false)
                      setSearch('')
                    }}
                    className="flex w-full items-center gap-3 px-3 py-2.5 text-left hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
                  >
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium">{o.productName}</span>
                      <span className="block truncate text-xs text-muted-foreground">
                        {o.sku}
                        {o.variantName && ` · ${o.variantName}`}
                      </span>
                    </span>
                    {warehouseId && (
                      <span className={cn('shrink-0 text-sm tabular', o.available <= 0 ? 'text-destructive' : 'text-muted-foreground')}>
                        {formatQuantity(o.available)} {o.unitOfMeasure}
                      </span>
                    )}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </DialogContent>
    </Dialog>
  )
}

const STATUS_STYLES: Record<StockStatus, { label: string; className: string }> = {
  InStock: { label: 'In stock', className: 'border-success/30 text-success' },
  Low: { label: 'Low', className: 'border-warning/40 bg-warning/10 text-warning' },
  Out: { label: 'Out', className: 'border-destructive/30 bg-destructive/10 text-destructive' },
}

export function StockStatusBadge({ status }: { status: StockStatus }) {
  const style = STATUS_STYLES[status]
  return (
    <Badge variant="outline" className={style.className}>
      {style.label}
    </Badge>
  )
}

const INCOMING: InventoryMovementType[] = ['StockIn', 'AdjustmentIncrease', 'TransferIn', 'PurchaseReceipt']

export function MovementTypeBadge({ type }: { type: InventoryMovementType }) {
  return (
    <Badge variant="outline" className={INCOMING.includes(type) ? 'border-success/30 text-success' : 'border-destructive/25 text-destructive'}>
      {MOVEMENT_LABELS[type]}
    </Badge>
  )
}

/** "+10" in green / "−3" in red, so direction reads at a glance. */
export function QuantityChange({ value }: { value: number }) {
  return (
    <span className={cn('font-semibold tabular', value > 0 ? 'text-success' : 'text-destructive')}>
      {value > 0 ? '+' : '−'}
      {formatQuantity(Math.abs(value))}
    </span>
  )
}
