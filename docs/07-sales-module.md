# Phase 4, Module 4: Customers and Sales (with stock reservation)

## What it does
- **Customers**: add, edit, archive, restore. Each one has a code (unique per company), contact details, payment
  terms and notes. The customer page shows open orders, **total sales** (the value actually *shipped*, not just
  ordered) and the order history.
- **Sales orders**, numbered `SO-000001`. Each order has a customer, the warehouse it ships from, an order date, and
  lines (variant, quantity, unit price). The price defaults to the product's selling price.
- **Shipments** (delivery receipts), numbered `DR-000001`. An order can ship in several parts.

## Lifecycle
```
Draft ──confirm──► Confirmed ──ship──► PartiallyFulfilled ──ship──► Fulfilled
  │                    │                     │
  └──────── cancel (reason required; releases whatever is still reserved) ──► Cancelled
```
| Action | Permission | Allowed from | What happens to stock |
|---|---|---|---|
| Create / edit | `sales.create` | Draft | nothing (a draft promises nothing) |
| Confirm | `sales.create` | Draft | **reserved** += qty for every line |
| Ship | `sales.fulfil` | Confirmed, PartiallyFulfilled | on hand −= qty **and** reserved −= qty |
| Cancel | `sales.cancel` | Draft, Confirmed, PartiallyFulfilled | reserved −= what hasn't shipped yet |

Sales Staff can create, confirm and ship, but not cancel. Inventory Staff can only ship, and only from their
assigned warehouses. Owners and Managers can do everything.

## Reservation: the key idea
Each warehouse stock row has three numbers:
```
on hand    = physically on the shelf
reserved   = promised to confirmed orders, not shipped yet
available  = on hand − reserved   ← the ONLY number anyone may sell, transfer or issue
```
- **Confirm** checks *available* and reserves. If any line is short, **nothing** is reserved: one transaction, error
  `INSUFFICIENT_STOCK` naming the SKU, and the order stays a draft.
- **Manual stock-out, transfers and adjustments** already use *available* (Module 2), so they can't take reserved
  units. A stock count can't go below the reserved quantity either.
- **Ship** takes the units out of on hand and the reservation together (`WarehouseStock.ShipReserved`), so available
  doesn't change when you ship.
- **Cancel** releases only the part that hasn't shipped. Shipped goods stay shipped.

## The guarantees
1. **No over-promising under concurrency.** Confirming locks the stock rows (`SELECT … FOR UPDATE`, in id order),
   the same engine as stock-out. Five orders racing for the last 10 units (4 each) → exactly 2 confirm, and the
   others get 422 (`Concurrent_confirmations_never_reserve_more_than_is_on_hand`).
2. **No double shipment.** Every shipment stamps `LastShippedAt` on the order, which changes its `xmin` version.
   Two simultaneous shipments of the same order → one succeeds, the other gets 409 or 422, and nothing is
   double-counted. The endpoint is also `[Idempotent]`, and the Ship dialog reuses its key on retry
   (a frontend test checks this).
3. **All-or-nothing.** The DR number, the order update, the stock lock and change, and the ledger lines all run in one
   `ExecuteInTransactionAsync`.
4. **Cost of goods sold.** Each shipment line stores the warehouse's average cost at that moment. The ledger gets a
   `SalesFulfilment` line (negative quantity, reference `SO-x / DR-y`). The order page shows revenue, COGS and
   gross margin for shipped goods, but only to roles with `inventory.view_cost`.

## API
```
GET    /api/v1/customers?search=&isActive=&sort=&page=
GET    /api/v1/customers/{id}
POST   /api/v1/customers                    PUT /api/v1/customers/{id}
POST   /api/v1/customers/{id}/archive       POST /api/v1/customers/{id}/restore

GET    /api/v1/sales-orders?status=&customerId=&warehouseId=&search=&sort=&page=
GET    /api/v1/sales-orders/{id}            (items include "available" in the order's warehouse)
POST   /api/v1/sales-orders                 PUT /api/v1/sales-orders/{id}     (draft only, with version)
POST   /api/v1/sales-orders/{id}/confirm
POST   /api/v1/sales-orders/{id}/cancel     { reason }
POST   /api/v1/sales-orders/{id}/shipments  { lines: [{ itemId, quantity }], note }   Idempotency-Key required
```
The inventory item picker (`/inventory/variants`) now also returns `sellingPrice`.

## Frontend
- `/customers`, `/customers/:id`: list, add/edit dialog, archive/restore, order history. Customers is now in the
  sidebar.
- `/sales`: status filter chips and search (kept in the URL), plus a shipped-progress bar per order.
- `/sales/new`, `/sales/:id/edit`: the draft form. Each line shows what's available in the chosen warehouse and warns
  when the quantity is higher ("not enough to confirm"). Changing the warehouse clears the hint, since it no longer
  applies.
- `/sales/:id`: the actions shown depend on status and permission.
  - A draft that can't be fully covered shows a warning before you confirm.
  - The Ship dialog is pre-filled with everything still to ship.
  - Cancel becomes "Cancel the rest" once something has shipped.
  - Also shows the profit card and the shipment history.

## Tests
- Domain (unit): totals, confirm, partial and full shipment, over-shipment, what each kind of cancel releases, and
  `WarehouseStock` reserve, release and ship-reserved rules (14 tests).
- Integration (9 tests):
  - the full lifecycle, checking stock, reservation, ledger and profit
  - a short line makes confirm fail with nothing reserved
  - 5 concurrent confirms
  - reserved stock blocks a manual stock-out
  - 5 concurrent shipments
  - partial cancel releases only the remainder
  - status rules and draft version conflicts
  - roles and warehouse scoping
  - tenant isolation and unique customer code
- Frontend (5 tests):
  - the ship dialog pre-fills, blocks over-shipping, and reuses the key on retry
  - Cancel is hidden without permission
  - the short-stock warning on drafts
  - required fields on the form

## Demo data
- **Customers:** 5.
- **Orders:**
  - a month of fulfilled orders (history for the upcoming dashboard)
  - one partially shipped
  - one confirmed, which shows as reserved stock on the Inventory page
  - one draft
  - one cancelled
- **Numbering:** `SyncSequencesAsync` moves the SO and DR counters past the seeded numbers.
