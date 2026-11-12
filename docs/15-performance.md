# M12, step 2: Performance pass

## How it was measured
- **Data:** a copy of the dev database, with the demo company inflated to what a busy SMB has after a year or two:

  | Data | Rows |
  |---|---|
  | Products | 1,692 (×3 warehouses of stock) |
  | Ledger movements, spread over 12 months | 300,000 |
  | Sales orders (all shipped) | 20,000, with 60,000 lines |
  | Customers | 2,000 |
  | Audit entries | 200,000 |
  | Notifications | 30,000 |

- **Metric:** server time (curl's time to first byte), median of 5 after a warm-up, API in Release mode on a laptop.
- **Don't measure with PowerShell.** The first pass used PowerShell's `Invoke-WebRequest` and showed the valuation
  report at 1.3 s. The API actually answered in ~40 ms; PowerShell just reads large responses slowly.
- **Read the query plan before adding an index.** EF Core's SQL logging gives each query's time, and `EXPLAIN ANALYZE`
  explains the slow ones.

## What was slow, and why
| Endpoint | Cause | Fix |
|---|---|---|
| **Sales report** | Loaded **every** shipped line of the period (60,000 rows with product and customer names) into memory, then grouped in C# | Groups by product or customer **in SQL**, fetches names for the result rows only. Totals: quantity, revenue and cost are sums of the groups; distinct orders is counted from shipments (20k rows) instead of lines (60k). Days still group in C#, because the company's time zone can't be applied portably in the Application layer, but now from numbers-only rows |
| **Dashboard (90 days)** | Also loaded 180 days of lines with a product name on each row, for a top-5 list | Numbers only; names fetched afterwards for the top products (plus ties for 5th place) |
| **Movements list** | The page count ran `COUNT(*)` over a four-way join (warehouse, variant, product, user) on every row | Filters on the ledger itself, counts that, then joins names onto the 25 rows of the page only |
| **Audit log** | Same pattern: the count went through the users join | Counted on its own, page joined afterwards |

## Results (300k-movement dataset)
| Endpoint | Before | After |
|---|---|---|
| Sales report by product, 1 year | 464 ms | **183 ms** |
| Sales report by day, 1 year | 451 ms | **97 ms** |
| Dashboard, 90 days | 213 ms | **98 ms** |
| Dashboard, 30 days | 134 ms | **69 ms** |
| Movements, one warehouse | 126 ms | **25 ms** |
| Movements, all | 171 ms | **99 ms** |
| Audit log | 107 ms | **79 ms** |
| Stock list, products, customers, orders, notifications | 9–35 ms | 7–27 ms |

The "after" runs were on a quieter machine, so part of the change in the small numbers is noise. The big drops come
from the fixes above.

## Looked at, and left alone (with reasons)
- **`COUNT(*)` over the whole ledger (~90 ms for 300k rows).**
  - The test company owns the entire table, so PostgreSQL correctly chooses a sequential scan. No index makes counting
    300k rows free.
  - On Windows about half of it is parallel-worker start-up.
  - The real fix would be "10,000+" style capped counts. That changes pagination in every list, which isn't worth it
    for a ~100 ms page.
- **Audit filter by record type (~105 ms) and the filter options (`DISTINCT entity_type`, ~80 ms).**
  - An index would speed these up but slow down every audited write, and audited writes happen on every change.
  - These are rarely used admin screens, so the index isn't worth the cost.
- **Indexes in general:** every list, foreign key and date-range query was already covered. The plans showed no
  missing index that the data actually needed.
- **N+1:** none found. Correlated subqueries in list projections (customer totals, user warehouses, platform counts)
  are single SQL statements, limited to the 20–25 rows of a page.

## Notes for the real deployment
- **Neon's free tier has a slower, shared CPU,** so expect these numbers ×2–3.
- **Neon also cold-starts after idle,** and that dominates the first request (~1 s). The frontend already retries
  during warm-up (`ServerWakingMessage`).
- **The sales report caps at 5,000 rows** (`truncated: true`), so even a very large company can't produce a
  multi-megabyte response.

## Tests
`ReportsTests.Sales_report_groups_match_and_one_order_counts_once_in_the_total`:
- One order with two products: each product's row counts it once.
- The total counts it once, not twice.
- Revenue and cost totals match the line-level arithmetic, grouped by product and by day.
