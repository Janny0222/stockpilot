# M12, step 1: Security review

Three independent read-only reviews ran in parallel:
1. Authorization and tenant isolation.
2. Authentication, sessions, infrastructure and configuration.
3. The React frontend and the Cloudflare Pages proxy.

Every finding was checked against the code before anything was changed. Each fix has a regression test, mostly in
`SecurityRegressionTests.cs`.

**No High findings, and no cross-tenant leak anywhere.** Tenant filters, the write guard, warehouse scope on almost
every path, cost hiding, refresh-token rotation and reuse detection, cookies, JWT validation, CSP and XSS sinks were
all confirmed correct.

## Fixed
| # | Severity | Finding | Fix | Test |
|---|---|---|---|---|
| 1 | Medium | `GET /inventory/variants?warehouseId=` (the stock-form type-ahead) didn't check warehouse scope, so Inventory Staff could read any warehouse's stock | Checks access first; 404 otherwise | `Inventory_staff_cannot_read_stock_of_a_warehouse_they_are_not_assigned_to` |
| 2 | Medium | **Image decompression bomb:** a flat-colour PNG of a few KB passes the 2 MB limit, decodes to 160 MB, and was copied twice more. A couple of those could crash the 512 MB instance, and anyone can register a company | 16 MP limit read from the header; no full-size copies; one decode at a time, server-wide | `A_tiny_file_that_decodes_to_a_huge_image_is_refused_before_decoding` |
| 3 | Medium | **Invite spam:** any new company could send many invitation emails from our domain, with its name as the text | Per-company limit of 30 invites (and re-sends) per hour | `Invitations_are_capped_per_company` |
| 4 | Medium | **Direct origin access:** calling the Render URL directly bypasses the proxy, so the caller picks their own X-Forwarded-For and dodges the per-IP sign-in limits | `ForwardedHeaders:RequireProxySecret=true` refuses requests without the proxy secret (except `/health`); a secret under 32 characters now stops the app instead of being silently ignored | Middleware tests in `TrustedProxyTests.cs` |
| 5 | Medium | **Cached data survives an expired session:** the cache was cleared only on a deliberate sign-out, so the next person on the same browser could briefly see the previous company's data | The cache is cleared whenever the signed-in user changes | `providers.test.tsx` |
| 6 | Low | **Sorting leaks cost:** Sales and Inventory Staff could sort the stock list by value, and the PO list by total, to learn the cost ranking without seeing the numbers | Cost sorts ignored without `inventory.view_cost` | `Sorting_by_value_does_not_reveal…` |
| 7 | Low | **Cost written by people who can't see it:** Inventory Staff could type a unit cost on stock-in and move average cost, stock value and COGS | Their stock-in uses the catalogue cost | `Inventory_staff_cannot_move_the_average_cost…` |
| 8 | Low | **Cost in notification text:** the PO approval notification included the PO total | Shows the number of lines instead (amounts are cost data) | `NotificationsTests` |
| 9 | Low | **Idempotency keys not tied to a user:** another user in the same company replaying a key got the first user's stored response | The key belongs to its first user; anyone else gets 409 | `An_idempotency_key_cannot_replay_another_users_response` |
| 10 | Low | **Forgot-password timing:** it awaited the mail server, so real accounts answered measurably slower | The email is sent without awaiting | n/a |
| 11 | Low | **Sign-in links in hosted logs:** the Log email provider wrote the whole body (reset and invite links) anywhere it was used | The body is logged only in Development | n/a |
| 12 | Low | **Login `returnTo`:** `/\evil.example` and `/<tab>/evil.example` turn into `//evil.example` in browsers | Resolved with `new URL`, same origin required | `guards.test.tsx` |
| 13 | Low | **Unencoded ids in links:** notification and audit links put ids into paths unencoded | `encodeURIComponent` | n/a |
| 14 | Low | **Pages proxy:** `/api/%2e%2e/…` could reach non-API paths upstream; `Forwarded` and `X-Original-*` were passed through | Only `/api/*` is forwarded; those headers are stripped too | n/a |
| 15 | Low | **CSV download:** a malformed filename broke it | Safe decode with a fallback | n/a |
| 16 | Low | **Dependencies:** an unused `cn` package, and a caret range on SignalR | Removed / pinned to 8.0.7 | n/a |
| 17 | Low | **CI token:** workflows used the default token scope | `permissions: contents: read` | n/a |
| 18 | Low | **docker-compose:** Postgres (password "postgres") and Mailpit were published on all network interfaces | Bound to 127.0.0.1 | n/a |

## Accepted, with reasons
- **Swagger in production.** It's a portfolio API, and the docs are part of the showcase. Every endpoint still
  requires auth and permissions. Set `Swagger__Enabled=false` to hide it.
- **`EMAIL_TAKEN` on registration.** One account per email (assumption A1) means registration has to say so. Sign-in
  and password reset still don't reveal anything.
- **Lockout as a weapon, and a lockout counter race.**
  - Five wrong passwords lock an account for 15 minutes, so anyone who knows an email can keep it locked.
  - Parallel failed logins can lose counter updates, giving an attacker a few extra guesses.
  - The per-IP sign-in limit (now enforced behind the proxy, finding 4) is the main defence. This gets revisited for
    the public demo accounts in the deploy step.
- **Access token in the SignalR query string.** Browsers can't put headers on a WebSocket.
  - It's accepted only on the hub path, the token lives 15 minutes, and our own logs record paths only.
  - It may still appear in Cloudflare or Render edge logs.
- **Job failure messages shown to the Super Admin.** That's the platform owner, who already has the server logs.
- **Single-use races on email links.** Two simultaneous submissions of the same reset or invite link could both
  succeed. The outcome is the same either way (the person sets their password).
- **For custom roles (Phase 5), not reachable today:** sales-order and PO create/update don't check warehouse scope,
  and draft confirm/cancel check it before the transaction. No built-in role with those permissions is warehouse
  scoped. Add the checks before custom roles ship.

## Deploying these fixes
On Render, add `ForwardedHeaders__RequireProxySecret=true`. The Pages `PROXY_SECRET` must equal
`ForwardedHeaders__ProxySecret`, otherwise every call returns 403 `DIRECT_ACCESS_BLOCKED`. See `docs/deployment.md`.
