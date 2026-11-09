# Phase 4, Module 2: Warehouses and Inventory (the stock engine)

## What it does
- **Warehouses**: add, edit, archive (only when empty, never the default), restore, make default.
  Each warehouse page shows its stock.
- **Stock levels** per variant per warehouse: on hand, reserved, available, reorder level, status (In stock / Low /
  Out), and average cost and value (cost permission only). Filter by warehouse, search, "low or out only".
- **Stock in / stock out**: multi-line entries with a reference and note. Stock-in takes a unit cost (defaults to
  the catalogue cost price).
- **Adjustments**: you enter the COUNTED quantity, and the system records the difference with a reason
  (count correction, damaged, lost, found, expired, other).
- **Transfers**: an atomic move between two warehouses, numbered `TR-000001`.
- **Movement history**: the immutable ledger, filterable by warehouse, type and search.

## The four guarantees, and how each is achieved

### 1. No overselling under concurrency
```
BEGIN
  INSERT … ON CONFLICT DO NOTHING      -- make sure a balance row exists
  SELECT … ORDER BY id FOR UPDATE      -- lock every row this operation touches, in id order
  domain: stock.Issue(qty)             -- throws INSUFFICIENT_STOCK if available < qty
  INSERT inventory_transactions …      -- ledger line(s)
COMMIT                                 -- locks released
```
A second request for the same item **waits** at the `FOR UPDATE` until the first commits, then reads the
updated quantity. Proved by `Fifty_concurrent_stock_outs_never_oversell`: 50 parallel requests against a stock of
10 → exactly 10 succeed. A mutation test (removing `FOR UPDATE`) makes it fail.

**Why lock in id order?** Transfer A→B locks rows {a, b}; transfer B→A locks {b, a}. If each grabbed "its first row"
first, they'd wait on each other forever (deadlock). Sorting means both try `a` first, so one simply queues.
`Concurrent_transfers_in_opposite_directions_do_not_deadlock…` runs 30 of them at once.

### 2. All-or-nothing
Each operation is ONE transaction (`IAppDbContext.ExecuteInTransactionAsync`). If line 3 of a 5-line stock-out lacks
stock, lines 1–2 are rolled back too. With Neon/Npgsql retries enabled, the **whole** transaction is replayed on a
transient failure (deadlock, dropped connection), never a single statement.

### 3. No double processing
Every stock write requires an `Idempotency-Key` header (`[Idempotent]` filter):

| Situation | Result |
|---|---|
| New key | claim it (row in `idempotency_records`), execute, store the response |
| Same key, same request, finished | **replay the stored response** (header `Idempotent-Replayed: true`) |
| Same key, still running | 409 `IDEMPOTENCY_IN_PROGRESS` |
| Same key, different body | 409 `IDEMPOTENCY_KEY_REUSED` (client bug) |
| Request failed | key released, so the client may retry with it |

The frontend creates one key per **user action** (`useIdempotencyKey`) and only renews it after success, so
pressing "Save" twice, or retrying after a network error, can't move stock twice. Responses are stored as `text`,
not `jsonb`, because jsonb re-orders keys and a replay must be byte-identical (a test caught this).

### 4. The ledger always reconciles
`warehouse_stocks` is a snapshot. `inventory_transactions` is the truth: rows are never updated or deleted, and
corrections are new movements. Tests assert `SUM(quantity_change) == quantity_on_hand` for every balance row after
concurrent operations.

## Costing: weighted average
`newAvg = (onHand × avg + qty × unitCost) / (onHand + qty)`. Example: 10 @ ₱100 + 30 @ ₱120 → 40 @ ₱115.
Outgoing stock is valued at the current average (needed later for cost of goods sold). Transfers carry the
source's average cost to the destination.

## Security notes
- Inventory Staff can only move stock in **assigned** warehouses (both ends of a transfer). Others get a 404.
- Adjustments require `inventory.adjust` (Owner and Manager): shrinkage write-offs are sensitive.
- Cost and value fields are `null` in API responses without `inventory.view_cost`.
- The raw SQL in `StockLocker` uses interpolated `FormattableString`, so every value is a bound parameter.

## Tests added
Domain (average cost, insufficient stock, adjust-to-count, negative policy, transfer rules) plus 12 integration
tests: atomic multi-line rollback, **50-way concurrency**, **opposite-direction transfers without deadlock**,
sequential transfer numbers and cost carry-over, idempotency (replay, reuse, missing key, release on failure),
adjustments and permissions, warehouse scoping, cross-company isolation, cost hiding, archive rules.
Frontend: adjustment difference preview, Idempotency-Key reuse on retry.
