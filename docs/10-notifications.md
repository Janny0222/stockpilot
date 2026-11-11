# Phase 4, Module 6b: Notifications

In-app notifications for what needs attention: stock running out, approvals and orders to ship. This is the MVP from
the Phase 1 plan: a list, an unread count kept fresh by polling, and per-person preferences. Live push (SignalR),
email and a daily digest come in M11.

## What triggers a notification, and who gets it
| Type | When | Who (permission, and warehouse scope) | Includes the person who did it? |
|---|---|---|---|
| `LowStock` | An item gets **worse**: in stock → low (≤ reorder level), or anything → out (≤ 0 available) | `purchases.manage` (the people who reorder), who can see that warehouse | Yes |
| `PurchaseOrderAwaitingApproval` | A PO is submitted | `purchases.approve` | No |
| `PurchaseOrderApproved` | A PO is approved | `purchases.receive`, who can see the PO's warehouse | No |
| `SalesOrderReadyToShip` | A sales order is confirmed (stock reserved) | `sales.fulfil`, who can see the order's warehouse | No |
| `UserJoined` | An invitee accepts | `users.manage` | n/a |

- **Who's left out:** deactivated users, people who haven't accepted their invitation yet, and anyone who turned the
  type off.
- **Workflow events skip the person who acted:** you don't need telling what you just did. Low stock includes them,
  because the person who took the last units should know too.
- **Who hears about low stock:** Inventory Staff don't get it by default. They can't buy, and the people who can
  reorder do get it. This is a matrix decision, so changing it only means changing the type's permission in
  `NotificationCatalog`.

## Low stock: once per problem
`LowStockAlerts.CheckAsync` runs on the locked stock rows right before `SaveChanges`. It compares each row's original
*available* (on hand − reserved) with the new one.

- **When it fires:** only when the item moves to a worse bucket (in stock 0, low 1, out 2), using the same buckets as
  the stock list.
  - 20 → 13 with a reorder level of 15: **alert**.
  - 13 → 11: nothing (already low).
  - 11 → 0: **alert** (now out).
  - Restocking and dropping again alerts again, because it's a new problem.
- **Where it's checked:** every path that can lower *available*. Stock out, adjustments, transfers (source side) and
  **confirming a sales order**, because a reservation lowers *available*. Shipping doesn't change *available* (it
  ships out of the reservation), so it doesn't need a check.
- **One notification per warehouse per operation:** a 20-line order that tips 4 items gives one alert ("4 items need
  reordering in Manila: Out of stock: … Running low: …") with up to 5 SKUs listed.
- **Clicking it** opens that warehouse's stock list, filtered to low items (`/inventory?warehouse=…&low=1`).
- **The message is a snapshot** of the moment it crossed ("13 available"), not a live number.

## Written with the change, in the same transaction
Notification rows are added to the same unit of work as the change they describe:
- A rolled-back change (insufficient stock, version conflict) leaves no notification behind.
- A committed one is never missing its notification.
- A transient-failure retry of the whole transaction starts from a clean change tracker, so it doesn't duplicate rows.

This is why there's no separate "send after commit" step yet. When SignalR arrives it will only *announce* rows that
already exist, so a missed push is recovered by the next list request.

`UserJoined` is the one exception to "the current tenant picks the audience". The accept-invitation request is
anonymous, so `AcceptInvitationHandler` (in the Auth folder, which the architecture test allows) finds the recipients
with the tenant filters bypassed and the invitee's company named explicitly.

## API (`/api/v1/notifications`, any signed-in user, always your own)
| | |
|---|---|
| `GET /notifications?unreadOnly=&page=` | newest first |
| `GET /notifications/unread-count` | one indexed COUNT: `(recipient_user_id, read_at, created_at)` |
| `POST /notifications/{id}/read` | someone else's id (same company or not) is a 404 |
| `POST /notifications/read-all` | one `UPDATE … WHERE recipient = me AND read_at IS NULL`, returns how many |
| `GET/PUT /notifications/preferences` | only the types your role can receive. Opt-out rows: no row means on |

Notifications and preferences aren't written to the audit log. They're messages about changes that are already
audited, and auditing every "mark as read" would be noise.

## Frontend
- **Bell in the header.**
  - Shows the unread count, capped at "9+" (the accessible name has the real number).
  - Polls every 60 s, only while the tab is visible; TanStack pauses it in the background.
  - The menu loads the latest 6 when opened.
  - Rows, "Mark all read" and the footer links are Radix menu items, so arrow keys work.
  - Opening a notification marks it read and goes to the record.
- **`/notifications`:** All or Unread, paged, with Mark all read.
- **Settings → Notifications:** one switch per type, saved on toggle. Everyone has this tab.
- **Entity links:** `lib/links.ts` now owns the "(entityType, id) → page" map for notifications and the audit log.
  The backend never knows frontend URLs.

## Tests
- **Integration (6):**
  - low stock fires once per problem and only for people who reorder (Inventory Staff excluded)
  - a sales-order reservation tipping into low stock, plus "ready to ship" to the right warehouse and not the confirmer
  - PO approval → approvers, then the receiving warehouse
  - the inbox is private (404 across users and companies), with read and read-all
  - preferences list only relevant types, and turning one off stops it
  - "joined" on invitation accept
- **Frontend (5):**
  - the bell's count, mark-read on open and navigation
  - no repeat mark for read items
  - hidden for platform admins
  - the page's unread filter and mark-all
  - the preference switch
- **Test stability:** the suite was flaky on a busy machine because user-event typing tests hit the 5 s timeout and
  `findBy` hit its 1 s wait. `testTimeout` is now 15 s and `asyncUtilTimeout` 3 s. Assertions are unchanged; a broken
  page still fails.
