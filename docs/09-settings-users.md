# Phase 4, Module 6: Settings, users and the audit log

Milestone M10 is done when an Owner can invite a user, scope them to warehouses and deactivate them, end to end.
`SettingsTests.Owner_invites_scopes_and_deactivates_a_user_end_to_end` does exactly that through the public API.

Notifications (the other half of M10) come next, in their own step.

## Who can do what
| Area | Permission | Default holder |
|---|---|---|
| Your own account and sessions | none | everyone |
| Company settings | `company.manage` | Owner |
| Users, roles list | `users.manage` | Owner |
| Audit log | `audit.view` | Owner |

The frontend shows a Settings tab only when the role has its permission. The API checks every request anyway.

## Inviting someone (`POST /api/v1/users`)
1. The Owner enters an email, a name, a role and (for warehouse-scoped roles) one or more warehouses.
2. The account is created **without a password**, and a single-use link goes to that email:
   `/accept-invitation#token=…`. The link lasts 7 days (`Auth:InvitationTokenLifetime`).
3. The invitee opens it, sees the company and role (`POST /auth/invitation`), and picks a password
   (`POST /auth/accept-invitation`). That confirms the email and signs them straight in.

- **Why no password from the Owner:** nobody but the user ever knows it, and the link proves they own the inbox.
- **Why the token stays out of URLs and logs:** it travels in the URL fragment, which browsers never send to servers.
  The API receives it only in a POST body.
- **Failure messages:** an unknown, used or expired link, a deactivated user and a suspended company all get the
  same `TOKEN_INVALID` message. A link can't be used to probe accounts.
- **Resend:** `POST /users/{id}/resend-invitation` retires the old link. `INVITATION_ACCEPTED` if they already joined.
- **Before accepting:** an invited user can't sign in, because they have no password.
- **One account per email across every company** (assumption A1). An email used anywhere gives `EMAIL_TAKEN`. That
  global check lives in `Auth/Common/EmailAvailability.cs`, inside the folders the architecture test allows to
  bypass the tenant filter.
- **Already signed in on that browser:** the accept page drops the previous user's token first. Otherwise the audit
  log would credit that person with the new account's changes (found while testing in the browser).

## Roles and warehouse scope
- `GET /roles` lists the roles you can assign, with their permissions: the built-in tenant roles plus the company's
  own. **Super Admin is never assignable** from inside a company (400).
- Inventory Staff is *warehouse-scoped*, so at least one warehouse is required. The warehouses must belong to your
  company; another company's ID counts as missing.
- **Scope changes apply on the very next request,** because `WarehouseAccessService` reads them live.
- **Role changes take about a minute:** a new role rotates the user's security stamp. Their access token then fails
  session validation (cached up to 60 s), and the next silent refresh carries the new role. They stay signed in.
- **You can't change your own role** (`CANNOT_CHANGE_OWN_ROLE`). This stops the last Owner from demoting themselves
  and locking the company out of its own settings.

## Deactivating (`POST /users/{id}/deactivate`)
- The security stamp rotates, so their access token stops working at once.
- Every refresh token is revoked (`UserDeactivated`).
- A sign-in attempt gets `ACCOUNT_DISABLED`, but only after the password is proven.
- **Nothing is deleted.** Their name stays on every order, receipt and ledger line they recorded.
- **Reactivate** with `POST /users/{id}/activate`.
- **You can't deactivate yourself** (`CANNOT_DEACTIVATE_SELF`).

## Company settings (`GET/PUT /api/v1/settings/company`)
- **Editable:** name, time zone and the "allow negative stock" policy.
- **Currency** can change only until the first stock movement (`CURRENCY_LOCKED`), because every stored cost and
  price is in it.
- **Time zone:** the frontend offers the browser's list of IANA zones. `ReportClock` falls back to UTC for anything it
  can't resolve.
- **After a save,** the app reloads `/auth/me`, so the header and money formatting update at once.

## Audit log (`GET /api/v1/audit-logs`)
- **What's in it:** every entity change, written in the same transaction as the change itself (from Phase 2), plus
  sign-in and security events.
- **Filters:** person, action (`Created`, `Updated`, `Deleted`, or `Auth` for all sign-in events), record type, and
  company-local dates. `GET /audit-logs/filters` lists only the values that actually occur.
- **Changes come back parsed** as `{ field, old, new }`, so the UI can show a before/after table.
- **Never stored:** passwords, security stamps and token hashes are excluded at write time. A test checks they never
  appear in the output.
- **Tenant filter:** a company never sees another company's rows or platform-level events.
- The UI reads each entry as a sentence ("Maria Santos updated company settings · 1 field") and expands it to show
  the before/after table and the IP address. It links to the record when one exists.

## Tests
- **Integration (5):**
  - invite → accept → scope → re-scope → deactivate → reactivate
  - resend retires the old link
  - guard rails:
    - can't deactivate or demote yourself
    - Super Admin, foreign warehouses and taken emails are rejected
    - another company's user is a 404
    - Manager gets 403
  - company settings, including the currency lock
  - audit log contents and isolation
- **Frontend (9):**
  - tabs per permission
  - invite with the warehouse requirement, and an email that's taken
  - deactivate with confirmation
  - no self-demotion or self-deactivation
  - currency lock
  - audit entries as sentences with change tables
  - accepting an invitation (sent without the old token), and a dead link
