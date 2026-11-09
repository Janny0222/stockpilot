import { ArrowLeft, Ban, CheckCircle2, LoaderCircle, PackageCheck, Pencil, Send } from 'lucide-react'
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
import type { PurchaseOrderDetailDto } from '@/lib/api/types'
import { formatDate, formatMoney, formatQuantity, useCurrency } from '@/lib/format'
import { Permissions as P } from '@/lib/permissions'
import { useHasPermission } from '@/stores/auth-store'
import { usePurchaseOrder, usePurchaseOrderAction, useReceivePurchaseOrder } from '../api'
import { PoStatusBadge } from '../components/purchasing-parts'

const dateTime = new Intl.DateTimeFormat('en-PH', {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function PurchaseOrderDetailPage() {
  const { id = '' } = useParams()
  const query = usePurchaseOrder(id)
  const currency = useCurrency()
  const canManage = useHasPermission(P.Purchases.Manage)
  const canApprove = useHasPermission(P.Purchases.Approve)
  const canReceive = useHasPermission(P.Purchases.Receive)
  const action = usePurchaseOrderAction(id)
  const [receiving, setReceiving] = useState(false)
  const [cancelling, setCancelling] = useState(false)

  const back = (
    <Button asChild variant="outline">
      <Link to="/purchases">
        <ArrowLeft /> All orders
      </Link>
    </Button>
  )

  if (query.isPending) return <Skeleton className="h-96 max-w-5xl" />
  if (query.isError) {
    return (
      <>
        <PageHeader title="Purchase order" actions={back} />
        <Card className="shadow-card">
          <ErrorState error={query.error} />
        </Card>
      </>
    )
  }

  const po = query.data
  const nothingReceived = po.items.every((i) => i.quantityReceived === 0)
  const run = (type: 'submit' | 'approve', success: string) =>
    action.mutate(
      { type },
      {
        onSuccess: () => toast.success(success),
        onError: (e) => toast.error(toApiError(e).message),
      },
    )

  return (
    <>
      <PageHeader
        title={po.number}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <PoStatusBadge status={po.status} />
            <Link to={`/suppliers/${po.supplierId}`} className="font-medium text-foreground hover:underline">
              {po.supplierName}
            </Link>
            <span aria-hidden="true">→</span> {po.warehouseName}
          </span>
        }
        actions={
          <>
            {back}
            {po.status === 'Draft' && canManage && (
              <>
                <Button asChild variant="outline">
                  <Link to={`/purchases/${po.id}/edit`}>
                    <Pencil /> Edit
                  </Link>
                </Button>
                <Button onClick={() => run('submit', 'Submitted for approval.')} disabled={action.isPending}>
                  <Send /> Submit
                </Button>
              </>
            )}
            {po.status === 'Submitted' && canApprove && (
              <ConfirmDialog
                trigger={
                  <Button disabled={action.isPending}>
                    <CheckCircle2 /> Approve
                  </Button>
                }
                title={`Approve ${po.number}?`}
                description={`This commits to buying from ${po.supplierName}${po.total !== null && po.total !== undefined ? ` for ${formatMoney(po.total, currency)}` : ''}. Lines can't be edited afterwards.`}
                confirmLabel="Approve order"
                onConfirm={() => run('approve', 'Order approved.')}
              />
            )}
            {(po.status === 'Approved' || po.status === 'PartiallyReceived') && canReceive && (
              <Button onClick={() => setReceiving(true)}>
                <PackageCheck /> Receive delivery
              </Button>
            )}
            {['Draft', 'Submitted', 'Approved'].includes(po.status) && nothingReceived && canManage && (
              <Button variant="outline" onClick={() => setCancelling(true)}>
                <Ban /> Cancel order
              </Button>
            )}
          </>
        }
      />

      <div className="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <Card className="gap-0 overflow-hidden py-0 shadow-card">
          <Table>
            <caption className="sr-only">Items on {po.number}</caption>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="pl-6">Item</TableHead>
                <TableHead className="text-right">Ordered</TableHead>
                <TableHead className="text-right">Received</TableHead>
                <TableHead className="hidden text-right sm:table-cell">Remaining</TableHead>
                {po.canViewCost && <TableHead className="hidden text-right md:table-cell">Unit cost</TableHead>}
                {po.canViewCost && <TableHead className="pr-6 text-right">Total</TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {po.items.map((i) => (
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
                  <TableCell className="text-right tabular">{formatQuantity(i.quantityReceived)}</TableCell>
                  <TableCell className="hidden text-right sm:table-cell tabular">
                    {i.quantityRemaining > 0 ? formatQuantity(i.quantityRemaining) : '—'}
                  </TableCell>
                  {po.canViewCost && <TableCell className="hidden text-right md:table-cell tabular">{formatMoney(i.unitCost, currency)}</TableCell>}
                  {po.canViewCost && <TableCell className="pr-6 text-right font-medium tabular">{formatMoney(i.lineTotal, currency)}</TableCell>}
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {po.canViewCost && (
            <div className="flex justify-end border-t px-6 py-4">
              <p className="text-sm text-muted-foreground">
                Order total <span className="ml-3 text-lg font-bold text-foreground tabular">{formatMoney(po.total, currency)}</span>
              </p>
            </div>
          )}
        </Card>

        <div className="grid gap-6">
          <Card className="shadow-card">
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-3 text-sm">
                <Row label="Order date" value={formatDate(po.orderDate)} />
                <Row label="Expected" value={formatDate(po.expectedDate)} />
                <Row label="Created by" value={po.createdBy ?? '—'} />
                {po.approvedAt && <Row label="Approved" value={`${dateTime.format(new Date(po.approvedAt))}${po.approvedBy ? ` by ${po.approvedBy}` : ''}`} />}
                {po.cancelledAt && <Row label="Cancelled" value={`${dateTime.format(new Date(po.cancelledAt))}: ${po.cancelReason}`} />}
              </dl>
              {po.notes && <p className="mt-4 border-t pt-3 text-sm whitespace-pre-line text-muted-foreground">{po.notes}</p>}
            </CardContent>
          </Card>

          <Card className="shadow-card">
            <CardHeader>
              <CardTitle>Deliveries</CardTitle>
            </CardHeader>
            <CardContent>
              {po.receipts.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing received yet.</p>
              ) : (
                <ol className="grid gap-4">
                  {po.receipts.map((r) => (
                    <li key={r.id} className="border-l-2 border-success/50 pl-3 text-sm">
                      <p className="font-semibold tabular">{r.number}</p>
                      <p className="text-muted-foreground">
                        {dateTime.format(new Date(r.receivedAt))}
                        {r.receivedBy && ` · ${r.receivedBy}`}
                      </p>
                      <ul className="mt-1 text-muted-foreground">
                        {r.items.map((i) => (
                          <li key={i.sku}>
                            {formatQuantity(i.quantity)} × {i.sku}
                          </li>
                        ))}
                      </ul>
                      {r.note && <p className="mt-1 italic text-muted-foreground">{r.note}</p>}
                    </li>
                  ))}
                </ol>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <ReceiveDialog po={po} open={receiving} onOpenChange={setReceiving} />
      <CancelDialog
        poNumber={po.number}
        open={cancelling}
        onOpenChange={setCancelling}
        onConfirm={(reason) =>
          action.mutateAsync({ type: 'cancel', reason }).then(() => {
            toast.success('Order cancelled.')
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

/** Pre-filled with everything still outstanding; edit down for a partial delivery. */
function ReceiveDialog({ po, open, onOpenChange }: { po: PurchaseOrderDetailDto; open: boolean; onOpenChange: (open: boolean) => void }) {
  // The idempotency key lives here (outside the form) so a retry after a network failure reuses it.
  const { key, renew } = useIdempotencyKey()
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        {/* DialogContent unmounts when closed, so the form starts fresh each time it opens. */}
        <ReceiveForm
          po={po}
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

function ReceiveForm({
  po,
  idempotencyKey,
  onDone,
  onCancel,
}: {
  po: PurchaseOrderDetailDto
  idempotencyKey: string
  onDone: () => void
  onCancel: () => void
}) {
  const receive = useReceivePurchaseOrder(po.id)
  const outstanding = po.items.filter((i) => i.quantityRemaining > 0)
  const [quantities, setQuantities] = useState<Record<string, string>>(() => Object.fromEntries(outstanding.map((i) => [i.id, String(i.quantityRemaining)])))
  const [note, setNote] = useState('')
  const [error, setError] = useState<string>()

  const submit = async () => {
    const lines = outstanding.map((i) => ({
      itemId: i.id,
      quantity: Number(quantities[i.id] || 0),
    }))
    const invalid = outstanding.find((i) => {
      const q = Number(quantities[i.id] || 0)
      return !Number.isFinite(q) || q < 0 || q > i.quantityRemaining
    })
    if (invalid) return setError(`${invalid.sku}: enter between 0 and ${formatQuantity(invalid.quantityRemaining)}.`)
    if (lines.every((l) => l.quantity === 0)) return setError('Enter a quantity for at least one item.')

    try {
      const result = await receive.mutateAsync({
        idempotencyKey,
        body: { lines, note: note || null },
      })
      toast.success(`${result.receiptNumber} recorded. Stock updated.`)
      onDone()
    } catch (e) {
      setError(toApiError(e).message)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Receive delivery</DialogTitle>
        <DialogDescription>Enter what actually arrived at {po.warehouseName}. Anything less stays open for a later delivery.</DialogDescription>
      </DialogHeader>
      <div className="grid gap-3">
        {error && <FormAlert tone="error">{error}</FormAlert>}
        {outstanding.map((i) => (
          <div key={i.id} className="grid grid-cols-[minmax(0,1fr)_7rem] items-center gap-3">
            <Label htmlFor={`rcv-${i.id}`} className="block min-w-0">
              <span className="block truncate font-medium">{i.productName}</span>
              <span className="block text-xs font-normal text-muted-foreground">
                {i.sku} · {formatQuantity(i.quantityRemaining)} {i.unitOfMeasure} outstanding
              </span>
            </Label>
            <Input
              id={`rcv-${i.id}`}
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
          <Label htmlFor="rcv-note">Note</Label>
          <Input
            id="rcv-note"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Optional, e.g. DR-55102, 2 boxes dented"
            className="h-10 bg-card"
          />
        </div>
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button onClick={submit} disabled={receive.isPending}>
          {receive.isPending && <LoaderCircle className="animate-spin" aria-hidden="true" />}
          Record receipt
        </Button>
      </DialogFooter>
    </>
  )
}

function CancelDialog({
  poNumber,
  open,
  onOpenChange,
  onConfirm,
}: {
  poNumber: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onConfirm: (reason: string) => Promise<void>
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <CancelForm poNumber={poNumber} onConfirm={onConfirm} onKeep={() => onOpenChange(false)} />
      </DialogContent>
    </Dialog>
  )
}

function CancelForm({ poNumber, onConfirm, onKeep }: { poNumber: string; onConfirm: (reason: string) => Promise<void>; onKeep: () => void }) {
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
        <DialogTitle>Cancel {poNumber}?</DialogTitle>
        <DialogDescription>The order stays in history as cancelled. This can't be undone.</DialogDescription>
      </DialogHeader>
      <div className="grid gap-2">
        {error && <FormAlert tone="error">{error}</FormAlert>}
        <Label htmlFor="cancel-reason">Reason</Label>
        <Input
          id="cancel-reason"
          autoFocus
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder="e.g. Supplier can't deliver"
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
