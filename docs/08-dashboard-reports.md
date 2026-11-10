# Phase 4, Module 5: Dashboard and Reports

Rule from Phase 1: **no hard-coded numbers.** Every figure on the dashboard and in the reports is computed from the
ledger and the documents (shipments, receipts, orders). Integration tests build a small, known history through the
public API and check the totals against hand-computed values.

## Dashboard (`GET /api/v1/dashboard?days=7|30|90&warehouseId=`)
One request. Each section comes back `null` when the caller's role can't see it:

| Section | Needs | Contents |
|---|---|---|
| `sales`, `salesTrend`, `topProducts` | `sales.view` | revenue and change vs the previous period of the same length, orders shipped, open orders, top 5 products. Gross profit and margin need `inventory.view_cost` |
| `stock`, `stockByWarehouse` | `inventory.view` | units on hand and reserved, items in stock, low and out-of-stock counts per warehouse. Value needs `inventory.view_cost` |
| `purchasing` | `purchases.view` | open POs, awaiting approval, overdue (past their expected date), units still incoming (and their value) |

Inventory Staff only ever see their assigned warehouses. Asking for a warehouse you can't access gives an *empty*
result, never someone else's numbers (same rule for reports).

### Revenue is recognised when goods ship
Sales figures come from **shipments** (DR lines), not from orders taken. Every peso of revenue then has a matching
cost of goods (the average cost stored on the shipment line), so the margin is real. A confirmed but unshipped
order shows under "Orders to ship" instead.

### Days are the company's days
"Today" and the daily buckets use the company's time zone (`tenants.time_zone`, set at registration from the
browser). For Manila (UTC+8), a sale at 17:30 UTC on Nov 9 counts toward **Nov 10**. See `ReportClock`, which is
unit-tested with a fixed +08:00 zone.

- **Docker:** chiseled images don't include tzdata, so the Dockerfile copies `/usr/share/zoneinfo` from the SDK image
  (about 4 MB). Without it, every zone silently falls back to UTC.
- **Windows dev:** with `InvariantGlobalization`, IANA ids like `Asia/Manila` can't be resolved, so local runs use
  UTC days. Linux (CI, Render) is correct. A Linux-only test pins it.

### Charts
- **Hand-built SVG, no chart library.** The Phase 1 plan said Recharts. We only need two shapes: grouped columns
  and ranked bars. A library would add about 100 KB to a bundle that's already over 500 KB, and the hand-built
  version follows the chart specs exactly.
- **Sales per day:** grouped columns, revenue (violet) and gross profit (aqua). Columns, not a line: most SMB days
  have zero sales, and a line would draw slopes between days that never happened. At 90 days the days are grouped
  into 7-day weeks.
- **Interaction:** hover, or focus the chart and use ←/→, to get a tooltip for each period. A hidden table carries
  the same numbers for screen readers.
- **Palette:** run through the dataviz validator against the white card. Lightness, chroma, CVD separation
  (ΔE 27.9) and contrast (≥ 3:1) all pass. The aqua is deliberately not the app's success green, because status
  colours stay reserved for status.
- **Best sellers and stock by warehouse:** ranked bars with the value at the tip.
- The "running low" and "latest movements" lists reuse the existing inventory endpoints.

## Reports (`GET /api/v1/reports/{key}`, CSV at `/{key}/csv`)
All five return the same table shape: `columns` (key, label, kind), `rows`, `totals` and `truncated`. So there is
**one** report screen and **one** CSV writer, and adding a report is just adding a query. Cost columns are dropped
(header and cells) for roles without `inventory.view_cost`.

| Key | Filters | Rows |
|---|---|---|
| `inventory-valuation` | warehouse | per item per warehouse: on hand, reserved, available, average cost, value |
| `low-stock` | warehouse | available ≤ reorder level, how far below, and what's already on approved POs |
| `sales` | from, to, warehouse, groupBy = product / customer / day | orders, quantity, revenue, COGS, gross profit, margin |
| `purchases` | from, to, warehouse, groupBy = supplier / product | deliveries (GRNs), quantity, value at PO cost |
| `stock-movements` | from, to, warehouse, type | the ledger, newest first |

- **Permissions:** `reports.view` to see a report, and `reports.export` for CSV. Owner and Manager have both.
- **Default period:** the last 30 days. The maximum is one year (`PERIOD_TOO_LONG`), and `from` must not be after
  `to` (`PERIOD_INVALID`).
- **Size cap:** 5,000 rows per report. Past that, `truncated: true` and the screen asks you to narrow the filters.

### CSV safety
- **UTF-8 with BOM:** Excel shows "Casa Home Depot – Quezon City" correctly instead of mojibake.
- **RFC 4180 quoting:** commas, quotes and newlines are handled.
- **Formula injection:** a text cell starting with `= + - @` or a tab gets a leading `'`. A product named
  `=HYPERLINK("http://evil…")` then stays text and doesn't become a live link when someone opens the export.
  An integration test checks this.
- **Download:** the browser fetches the file through the API client (it needs the bearer token) and saves it under
  the server's filename, e.g. `sales-20261012-20261110.csv`.

## Tests
- **Unit:** `ReportClock` (local-midnight boundaries, a late UTC sale landing on the next local day, UTC
  fallback), plus an IANA test that only runs on Linux.
- **Integration (5):**
  - dashboard totals match the transactions (revenue 900, COGS 600, margin 33.3%, value 1,400, 2 reserved, one
    point per day)
  - sections follow the role
  - reports agree with the dashboard
  - a foreign warehouse returns an empty report
  - 403, 404 and 422 rules
  - the CSV has a BOM and a header, formulas are neutralised, and Manager can export while Inventory Staff can't
- **Frontend (5):**
  - real figures with change vs the previous period
  - Sales Staff see no stock value, profit or purchasing
  - no ∞% when the previous period was empty
  - chart keyboard reading and the data table
  - the generic report table with totals, and Export hidden without permission
