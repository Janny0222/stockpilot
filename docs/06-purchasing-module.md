# Phase 4, Module 3: Suppliers and Purchasing

## What it does
- **Suppliers**: add, edit, archive, restore. Each one has a code (unique per company), contact details, payment
  terms (0 = cash), and notes. The supplier page shows open orders, the value received so far (cost permission
  only), and its purchase history.
- **Purchase orders**, numbered `PO-000001`. Each order has a supplier, a delivery warehouse, an order date, an
  optional expected date, and lines (variant, quantity, unit cost).
- **Receiving**, numbered `GRN-000001`. A delivery can be partial. Every receipt books stock into the warehouse at
  the PO's unit cost and writes one `PurchaseReceipt` ledger line per item.

## Lifecycle
```
Draft ──submit──► Submitted ──approve──► Approved ──receive──► PartiallyReceived ──receive──► Received
  │                   │                     │
  └──────────── cancel (reason required, only while nothing has been received) ──► Cancelled
```
| Action | Permission | Allowed from |
|---|---|---|
| Create / edit lines | `purchases.manage` | Draft only |
| Submit | `purchases.manage` | Draft (needs at least one line) |
| Approve | `purchases.approve` | Submitted |
| Receive | `purchases.receive` | Approved, PartiallyReceived |
| Cancel | `purchases.manage` | Draft, Submitted, Approved, with nothing received |

The rules are in the domain (`PurchaseOrder.Submit/Approve/Cancel/Receive`). The UI only hides buttons the server
would reject anyway.

## The guarantees
1. **No over-receipt.** `Receive` throws `OVER_RECEIPT` if a quantity exceeds what's still outstanding. A database
   check constraint (`quantity_received <= quantity_ordered`) backs it up.
2. **No double receipt under concurrency.** Every receipt updates `LastReceivedAt` on the PO, which bumps its `xmin`
   row version. If two people receive the same order at once, the second save gets `CONCURRENCY_CONFLICT` (409) and
   its whole transaction rolls back, including the stock and ledger lines. `Concurrent_receipts_…` fires 5 at once
   and exactly 1 succeeds.
3. **No double processing.** `POST /purchase-orders/{id}/receipts` is `[Idempotent]`. The receive dialog keeps one
   key until a success, so a retry after a timeout replays the stored response instead of booking the delivery
   twice. A frontend test checks that the key is reused.
4. **All-or-nothing.** The GRN number, the PO update, the stock row locks (same `IStockLocker`, in id order),
   the weighted-average cost update and the ledger all run in one `ExecuteInTransactionAsync`.
5. **Edits don't clobber.** Saving a draft sends `version`. A stale version gets a 409, and the form asks you to
   reload.

## Cost visibility
Unit costs, line totals, order totals and a supplier's received value are `null` in the API for roles without
`inventory.view_cost`, and the UI hides those columns.

## API
```
GET    /api/v1/suppliers?search=&isActive=&sort=&page=
GET    /api/v1/suppliers/{id}
POST   /api/v1/suppliers                    PUT /api/v1/suppliers/{id}
POST   /api/v1/suppliers/{id}/archive       POST /api/v1/suppliers/{id}/restore

GET    /api/v1/purchase-orders?status=&supplierId=&search=&sort=&page=
GET    /api/v1/purchase-orders/{id}
POST   /api/v1/purchase-orders              PUT /api/v1/purchase-orders/{id}     (draft only, with version)
POST   /api/v1/purchase-orders/{id}/submit | approve
POST   /api/v1/purchase-orders/{id}/cancel  { reason }
POST   /api/v1/purchase-orders/{id}/receipts  { lines: [{ itemId, quantity }], note }   Idempotency-Key required
```

## Frontend
- `/suppliers`, `/suppliers/:id`: list with search and sort, add/edit dialog, archive/restore, purchase history.
- `/purchases`: status filter chips and search, all kept in the URL. Shows a receiving progress bar per order.
- `/purchases/new`, `/purchases/:id/edit`: the draft form (RHF + Zod), using the inventory item picker. Unit cost
  defaults to the catalogue cost price.
- `/purchases/:id`: actions depend on status and permission. Includes the receive dialog, pre-filled with
  everything outstanding (lower it for a partial delivery), a cancel dialog with a reason, and the delivery history.

## Tests
- Domain (unit): status transitions, over-receipt, partial then full receipt, cancel rules.
- Integration: the full lifecycle into stock and the ledger, over-receipt, 5 concurrent receipts → 1 success,
  status rules, draft edit concurrency, role checks, cross-tenant supplier → 404, unique supplier code.
- Frontend: receive pre-fill and the over-quantity block, idempotency key reuse, hiding receive without permission,
  required fields on the PO form.

## Demo data
Five suppliers and five orders covering every status. `SyncSequencesAsync` moves the PO/GRN counters past the
seeded numbers, so the next new order is `PO-000006`.
