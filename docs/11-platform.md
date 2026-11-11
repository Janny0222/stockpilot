# Phase 4: Platform administration (Super Admin)

The Super Admin runs StockPilot itself. Assumption A11 sets the line: they see **companies as accounts**, never a
company's business data.

| Visible to the platform | Not visible |
|---|---|
| Company name, ID, status, currency, time zone, registration date | Products, prices, costs, stock, orders, customers, suppliers |
| Owner's name and email (who to contact) | Any page inside the company |
| Counts: active users, active warehouses, products | |
| Last sign-in across the company's users | |

`GET /api/v1/products` (and every other company endpoint) stays **403** for the Super Admin. A test pins it.

## Endpoints (`/api/v1/platform`)
| | Permission |
|---|---|
| `GET metrics`: companies, active/suspended, new in 30 days, users, users active in 7 days | `platform.metrics.view` |
| `GET tenants?search=&status=&sort=`: search by company name, ID or **owner email** | `platform.tenants.view` |
| `GET tenants/{id}` | `platform.tenants.view` |
| `POST tenants/{id}/suspend` `{ reason }` and `POST tenants/{id}/activate` | `platform.tenants.manage` |

- **Who has these permissions:** only the Super Admin role, which can't be assigned from inside a company. Owners get
  403 and anonymous callers get 401.
- **Why this folder can bypass the tenant filter:** the Super Admin has no tenant, so every query filter matches
  nothing. `Features/Platform` was already one of the folders the architecture test allows to call
  `IgnoreQueryFilters()`. Each query names the company explicitly.

## Suspending a company
- **Sign-in:** blocked with `TENANT_SUSPENDED`.
- **Tokens:** existing access and refresh tokens fail session validation, which checks the company's status, within
  the cache window (up to 60 s).
- **Data:** nothing is deleted. Reactivating restores everything as it was.
- **Reason:** required. It goes into the **company's own audit log** as `Platform.TenantSuspended`, so after
  reactivation the Owner can see who suspended them (shown as "StockPilot support") and why. Reactivation is logged
  as `Platform.TenantActivated`.
- **Already in that state:** suspending again gives `ALREADY_SUSPENDED`, and activating an active company gives
  `ALREADY_ACTIVE` (409).

## Frontend
- **`/platform`:** totals tiles, then a searchable and sortable company table.
- **`/platform/:id`:** account details and counts, with Suspend (a dialog that requires a reason) or Reactivate. A
  Super Admin without `platform.tenants.manage` gets a read-only view.
- **Landing page:** the Super Admin lands here; the "Companies" nav item is no longer "coming soon".

## Tests
- **Integration (3):**
  - account view with exact counts, search by owner email, no business data, 404 for an unknown ID
  - suspend → whole company locked out → `ALREADY_SUSPENDED` → reactivate → sign in again, with both events and the
    reason in the company's audit log
  - no company role, and no anonymous caller, reaches any platform endpoint
- **Frontend (3):** totals and search, suspend only with a reason then Reactivate, read-only without the manage
  permission.
