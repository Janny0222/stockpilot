import { ArrowLeft, Ban, CheckCircle2, LoaderCircle, Pencil, Truck } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { ConfirmDialog } from '@/components/shared/confirm-dialog'
import { ErrorState, PageHeader } from '@/components/shared/states'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { FormAlert } from '@/features/auth/components/form-alert'
import { useIdempotencyKey } from '@/hooks/use-idempotency-key'
import { toApiError } from '@/lib/api/errors'
import type { SalesOrderDetailDto } from '@/lib/api/types'
import { formatDate, formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { useSalesOrder, useSalesOrderAction, useShipSalesOrder } from '../api'
import { SoStatusBadge } from '../components/sales-parts'

const dateTime = new Intl.DateTimeFormat('en-PH', { dateStyle: 'medium', timeStyle: 'short' })

export function SalesOrderDetailPage() {
  const { id = '' } = useParams()
  const query = useSalesOrder(id)
  const currency = useCurrency()
  const canCreate = useHasPermission(P.Sales.Create)
  const canFulfil = useHasPermission(P.Sales.Fulfil)
  const canCancel = useHasPermission(P.Sales.Cancel)
  const action = useSalesOrderAction(id)
  const [shipping, setShipping] = useState(false)
  const [cancelling, setCancelling] = useState(false)

  const back = (
    <Button asChild variant="outline">
      <Link to="/sales">
        <ArrowLeft /> All orders
      </Link>
    </Button>
  )

  if (query.isPending) return <Skeleton className="h-96 max-w-5xl" />
  if (query.isError) {
    return (
      <>
        <PageHeader title="Sales order" actions={back} />
        <Card className="shadow-card">
          <ErrorState error={query.error} />
        </Card>
      </>
    )
  }

  const so = query.data
  const isDraft = so.status === 'Draft'
  const holdsStock = so.status === 'Confirmed' || so.status === 'PartiallyFulfilled'
  const shortLines = isDraft ? so.items.filter((i) => i.quantityOrdered > i.available) : []
  const anyShipped = so.items.some((i) => i.quantityShipped > 0)

  const confirm = () =>
    action.mutate(
      { type: 'confirm' },
      { onSuccess: () => toast.success('Order confirmed. Stock reserved.'), onError: (e) => toast.error(toApiError(e).message) },
    )

  return (
    <>
      <PageHeader
        title={so.number}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <SoStatusBadge status={so.status} />
            <Link to={`/customers/${so.customerId}`} className="font-medium text-foreground hover:underline">
              {so.customerName}
            </Link>
            <span aria-hidden="true">·</span> ships from {so.warehouseName}
          </span>
        }
        actions={
          <>
            {back}
            {isDraft && canCreate && (
              <>
                <Button asChild variant="outline">
                  <Link to={`/sales/${so.id}/edit`}>
                    <Pencil /> Edit
                  </Link>
                </Button>
                <ConfirmDialog
                  trigger={
                    <Button disabled={action.isPending}>
                      <CheckCircle2 /> Confirm order
                    </Button>
                  }
                  title={`Confirm ${so.number}?`}
                  description={
                    shortLines.length > 0
                      ? `${shortLines.map((i) => i.sku).join(', ')} ${shortLines.length === 1 ? "doesn't" : "don't"} have enough available stock, so confirming will fail until you restock or lower the quantity.`
                      : `This reserves the stock in ${so.warehouseName} for ${so.customerName}. Lines can't be edited afterwards.`
                  }
                  confirmLabel="Confirm and reserve"
                  onConfirm={confirm}
                />
              </>
            )}
            {holdsStock && canFulfil && (
              <Button onClick={() => setShipping(true)}>
                <Truck /> Ship
              </Button>
            )}
            {(isDraft || holdsStock) && canCancel && (
              <Button variant="outline" onClick={() => setCancelling(true)}>
                <Ban /> {anyShipped ? 'Cancel the rest' : 'Cancel order'}
              </Button>
            )}
          </>
        }
      />

      {shortLines.length > 0 && (
        <div className="mb-6 max-w-5xl">
          <FormAlert tone="error">
            Not enough available in {so.warehouseName} for {shortLines.map((i) => `${i.sku} (${formatQuantity(i.available)} available)`).join(', ')}.
          </FormAlert>
        </div>
      )}

      <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <Card className="gap-0 overflow-hidden py-0 shadow-card">
          <Table>
            <caption className="sr-only">Items on {so.number}</caption>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="pl-6">Item</TableHead>
                <TableHead className="text-right">Ordered</TableHead>
                <TableHead className="text-right">Shipped</TableHead>
                <TableHead className="hidden text-right sm:table-cell">{isDraft ? 'Available' : 'To ship'}</TableHead>
                <TableHead className="hidden text-right md:table-cell">Price</TableHead>
                <TableHead className="pr-6 text-right">Total</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {so.items.map((i) => (
                <TableRow key={i.id}>
                  <TableCell className="pl-6">
                    <Link to={`/products/${i.productId}`} className="font-medium hover:underline">
                      {i.productName}
                    </Link>
                    <p className="text-xs text-muted-foreground">
                      {i.sku}
                      {i.variantName && ` · ${i.variantName}`}
                    </p>
                  </TableCell>
                  <TableCell className="text-right tabular">
                    {formatQuantity(i.quantityOrdered)} {i.unitOfMeasure}
                  </TableCell>
                  <TableCell className="text-right tabular">{formatQuantity(i.quantityShipped)}</TableCell>
                  <TableCell className="hidden text-right sm:table-cell tabular">
                    {isDraft ? (
                      <span className={i.quantityOrdered > i.available ? 'font-medium text-destructive' : undefined}>{formatQuantity(i.available)}</span>
                    ) : i.quantityRemaining > 0 && so.status !== 'Cancelled' ? (
                      formatQuantity(i.quantityRemaining)
                    ) : (
                      '—'
                    )}
                  </TableCell>
                  <TableCell className="hidden text-right md:table-cell tabular">{formatMoney(i.unitPrice, currency)}</TableCell>
                  <TableCell className="pr-6 text-right font-medium tabular">{formatMoney(i.lineTotal, currency)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <div className="flex justify-end border-t px-6 py-4">
            <p className="text-sm text-muted-foreground">
              Order total <span className="ml-3 text-lg font-bold text-foreground tabular">{formatMoney(so.total, currency)}</span>
            </p>
          </div>
        </Card>

        <div className="grid gap-6">
          <Card className="shadow-card">
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-3 text-sm">
                <Row label="Order date" value={formatDate(so.orderDate)} />
                <Row label="Created by" value={so.createdBy ?? '—'} />
                {so.confirmedAt && (
                  <Row label="Confirmed" value={`${dateTime.format(new Date(so.confirmedAt))}${so.confirmedBy ? ` by ${so.confirmedBy}` : ''}`} />
                )}
                {so.cancelledAt && <Row label="Cancelled" value={`${dateTime.format(new Date(so.cancelledAt))}: ${so.cancelReason}`} />}
              </dl>
              {so.notes && <p className="mt-4 border-t pt-3 text-sm whitespace-pre-line text-muted-foreground">{so.notes}</p>}
            </CardContent>
          </Card>

          {so.profit && so.profit.revenue > 0 && (
            <Card className="shadow-card">
              <CardHeader>
                <CardTitle>Profit on shipped goods</CardTitle>
              </CardHeader>
              <CardContent>
                <dl className="grid gap-3 text-sm">
                  <Row label="Revenue" value={formatMoney(so.profit.revenue, currency)} />
                  <Row label="Cost of goods" value={formatMoney(so.profit.costOfGoods, currency)} />
                  <Row
                    label="Gross profit"
                    value={`${formatMoney(so.profit.grossProfit, currency)}${so.profit.marginPercent !== null ? ` (${so.profit.marginPercent}%)` : ''}`}
                  />
                </dl>
              </CardContent>
            </Card>
          )}

          <Card className="shadow-card">
            <CardHeader>
              <CardTitle>Shipments</CardTitle>
            </CardHeader>
            <CardContent>
              {so.shipments.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing shipped yet.</p>
              ) : (
                <ol className="grid gap-4">
                  {so.shipments.map((s) => (
                    <li key={s.id} className="border-l-2 border-success/50 pl-3 text-sm">
                      <p className="font-semibold tabular">{s.number}</p>
                      <p className="text-muted-foreground">
                        {dateTime.format(new Date(s.shippedAt))}
                        {s.shippedBy && ` · ${s.shippedBy}`}
                      </p>
                      <ul className="mt-1 text-muted-foreground">
                        {s.items.map((i) => (
                          <li key={i.sku}>
                            {formatQuantity(i.quantity)} × {i.sku}
                          </li>
                        ))}
                      </ul>
                      {s.note && <p className="mt-1 text-muted-foreground italic">{s.note}</p>}
                    </li>
                  ))}
                </ol>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <ShipDialog so={so} open={shipping} onOpenChange={setShipping} />
      <CancelDialog
        title={anyShipped ? `Cancel the rest of ${so.number}?` : `Cancel ${so.number}?`}
        description={
          holdsStock
            ? `What hasn't shipped is cancelled and its reserved stock goes back to available.${anyShipped ? ' Shipped goods stay shipped.' : ''} This can't be undone.`
            : "The draft stays in history as cancelled. This can't be undone."
        }
        open={cancelling}
        onOpenChange={setCancelling}
        onConfirm={(reason) =>
          action.mutateAsync({ type: 'cancel', reason }).then(() => {
            toast.success(holdsStock ? 'Order cancelled. Reserved stock released.' : 'Order cancelled.')
            setCancelling(false)
          })
        }
      />
    </>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="grid grid-cols-[6.5rem_1fr] gap-2">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  )
}

function ShipDialog({ so, open, onOpenChange }: { so: SalesOrderDetailDto; open: boolean; onOpenChange: (open: boolean) => void }) {
  // The key lives outside the form so a retry after a network failure reuses it (the server applies it once).
  const { key, renew } = useIdempotencyKey()
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        {/* DialogContent unmounts when closed, so the form starts fresh each time it opens. */}
        <ShipForm
          so={so}
          idempotencyKey={key}
          onDone={() => {
            renew()
            onOpenChange(false)
          }}
          onCancel={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  )
}

/** Pre-filled with everything still to ship; lower it for a partial delivery. */
function ShipForm({ so, idempotencyKey, onDone, onCancel }: { so: SalesOrderDetailDto; idempotencyKey: string; onDone: () => void; onCancel: () => void }) {
  const ship = useShipSalesOrder(so.id)
  const outstanding = so.items.filter((i) => i.quantityRemaining > 0)
  const [quantities, setQuantities] = useState<Record<string, string>>(() => Object.fromEntries(outstanding.map((i) => [i.id, String(i.quantityRemaining)])))
  const [note, setNote] = useState('')
  const [error, setError] = useState<string>()

  const submit = async () => {
    const lines = outstanding.map((i) => ({ itemId: i.id, quantity: Number(quantities[i.id] || 0) }))
    const invalid = outstanding.find((i) => {
      const q = Number(quantities[i.id] || 0)
      return !Number.isFinite(q) || q < 0 || q > i.quantityRemaining
    })
    if (invalid) return setError(`${invalid.sku}: enter between 0 and ${formatQuantity(invalid.quantityRemaining)}.`)
    if (lines.every((l) => l.quantity === 0)) return setError('Enter a quantity for at least one item.')

    try {
      const result = await ship.mutateAsync({ idempotencyKey, body: { lines, note: note || null } })
      toast.success(`${result.shipmentNumber} recorded. Stock updated.`)
      onDone()
    } catch (e) {
      setError(toApiError(e).message)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Ship order</DialogTitle>
        <DialogDescription>Enter what is leaving {so.warehouseName} now. Anything less stays reserved for a later delivery.</DialogDescription>
      </DialogHeader>
      <div className="grid gap-3">
        {error && <FormAlert tone="error">{error}</FormAlert>}
        {outstanding.map((i) => (
          <div key={i.id} className="grid grid-cols-[minmax(0,1fr)_7rem] items-center gap-3">
            <Label htmlFor={`ship-${i.id}`} className="block min-w-0">
              <span className="block truncate font-medium">{i.productName}</span>
              <span className="block text-xs font-normal text-muted-foreground">
                {i.sku} · {formatQuantity(i.quantityRemaining)} {i.unitOfMeasure} to ship
              </span>
            </Label>
            <Input
              id={`ship-${i.id}`}
              type="number"
              step="any"
              inputMode="decimal"
              min={0}
              className="h-10 bg-card text-right"
              value={quantities[i.id] ?? ''}
              onChange={(e) => setQuantities((q) => ({ ...q, [i.id]: e.target.value }))}
            />
          </div>
        ))}
        <div className="grid gap-1.5">
          <Label htmlFor="ship-note">Note</Label>
          <Input
            id="ship-note"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Optional, e.g. truck plate or courier tracking no."
            className="h-10 bg-card"
          />
        </div>
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button onClick={submit} disabled={ship.isPending}>
          {ship.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Record shipment
        </Button>
      </DialogFooter>
    </>
  )
}

function CancelDialog({
  title,
  description,
  open,
  onOpenChange,
  onConfirm,
}: {
  title: string
  description: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onConfirm: (reason: string) => Promise<void>
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <CancelForm title={title} description={description} onConfirm={onConfirm} onKeep={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function CancelForm({
  title,
  description,
  onConfirm,
  onKeep,
}: {
  title: string
  description: string
  onConfirm: (reason: string) => Promise<void>
  onKeep: () => void
}) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string>()
  const [pending, setPending] = useState(false)

  const submit = async () => {
    if (!reason.trim()) return setError('Give a reason for cancelling.')
    setPending(true)
    try {
      await onConfirm(reason.trim())
    } catch (e) {
      setError(toApiError(e).message)
    } finally {
      setPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
      <div className="grid gap-2">
        {error && <FormAlert tone="error">{error}</FormAlert>}
        <Label htmlFor="cancel-reason">Reason</Label>
        <Input
          id="cancel-reason"
          autoFocus
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="e.g. Customer changed their mind"
          className="h-10 bg-card"
        />
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={onKeep}>
          Keep order
        </Button>
        <Button variant="destructive" onClick={submit} disabled={pending}>
          {pending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Cancel order
        </Button>
      </DialogFooter>
    </>
  )
}
