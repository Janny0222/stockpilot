# Phase 3: Frontend Foundation (how it works and why)

## 1. Stack, and what each piece is for

| Concern | Tool | Why this one |
|---|---|---|
| Build / dev server | **Vite** | Instant start, hot reload, and a built-in proxy for `/api` |
| UI language | **React 19 + TypeScript** (strict) | Types catch contract drift before the browser does |
| Styling | **Tailwind v4** + design tokens in `src/index.css` | Components use semantic names (`bg-primary`), never raw hex |
| Components | **shadcn/ui** (Radix underneath) | Accessible primitives copied into `src/components/ui/`: owned code, not a dependency |
| Server state | **TanStack Query** | Caching, loading/error states, retries, invalidation |
| Client state | **Zustand** (`stores/auth-store.ts`) | Only "who is signed in". Everything from the API lives in TanStack Query |
| Forms | **React Hook Form + Zod** | Fast forms; Zod schemas mirror the server's FluentValidation rules |
| HTTP | **Axios** (`lib/api/client.ts`) | Interceptors for the bearer token and the 401 → refresh → retry flow |
| Routing | **React Router** | Nested layouts, route guards, breadcrumbs from route `handle`s |
| Tests | **Vitest + Testing Library + MSW** | MSW intercepts real HTTP, so tests run the actual API client |

## 2. Folder layout

```
src/
  app/           router, providers, layout (sidebar, header), guards, navigation
  components/
    ui/          shadcn primitives (generated, lightly adapted)
    shared/      app-wide pieces: DataTable, FormField, PageHeader, EmptyState, ConfirmDialog…
  features/      one folder per business area: api + pages + components + schemas
    auth/  account/  dashboard/  warehouses/
  lib/           api client, error normalization, permissions, utils
  stores/        Zustand stores
  types/         api.gen.ts: GENERATED from the backend OpenAPI document
  test/          MSW server, render helpers, setup
```

Feature folders mirror the backend's `Application/Features/*`, so "where's the warehouse code?" has the same
answer on both sides.

## 3. Authentication in the browser

```
Page load ─► bootstrapSession(): POST /auth/refresh (cookie sent automatically)
               ├─ 200 → access token in memory → GET /auth/me → status "authenticated"
               └─ 401 → status "unauthenticated" → router shows /login
Any request ─► interceptor adds "Authorization: Bearer <token>"
               └─ 401? → ONE shared refresh call → replay the request with the new token
                          └─ refresh fails → session cleared → login (with ?returnTo=…)
```

- **The access token is never written to `localStorage`.** It lives in a JavaScript variable (the Zustand store),
  so injected script can't read a persisted copy. The refresh token is an HttpOnly cookie that JavaScript can't
  read at all. (Verified in the browser: `localStorage`, `sessionStorage` and `document.cookie` are all empty.)
- **Single-flight refresh.** If 5 requests fail at once, they share one refresh. Five parallel refreshes would
  rotate the token five times, and the server's reuse detection would treat that as theft and revoke the session.
  A test proves it, and a mutation test showed it fails (3 refreshes instead of 1) when the guard is removed.
- **Same origin.** Vite proxies `/api` → `localhost:5078`, so cookies are first-party. In production a Cloudflare
  Pages Function does the same job (Phase 6).
- **Email links** put the token in the URL fragment (`#token=…`), which browsers never send to servers. The page
  reads it once, then removes it from the address bar.

## 4. Permissions in the UI
`/auth/me` returns the user's permission codes. The sidebar (`app/navigation.ts`) shows only modules the role can
open, and `RequirePermission` replaces a page with an explanation if someone types the URL directly.
**This is UX, not security.** The API rejects unauthorized calls on its own, which is why the backend tests matter
more than these.

## 5. Lists: server-side search, sort, paging
`DataTable` renders what the API returned and reports intents ("sort by code, descending"). Page, sort and search
live in the **URL** (`/warehouses?q=cebu&sort=code:desc`), so refresh, back/forward and shared links reproduce the
same view. Search is debounced (300 ms) so typing sends one request, not one per key.

## 6. Keeping frontend and backend in sync
```bash
npm run gen:api   # with the API running on :5078
```
regenerates `src/types/api.gen.ts`. If a C# DTO changes, `npm run typecheck` fails everywhere the frontend relies on
the old shape. While setting this up we found the API advertised integers as `number | string` (ASP.NET's lenient
number parsing). The backend now uses strict JSON numbers, and the types say `number`.

## 7. Design decisions vs. the reference image
Kept: dark navy sidebar with a violet active item, cool light canvas, white cards, colored icon tiles, ₱ formatting.
Changed:
- No "Upgrade to Pro" card (no billing feature).
- No company switcher (one company per user).
- No search bar or notification bell until those features exist.
- No sparklines or charts with invented numbers: real metrics arrive with the modules that produce them (Phase 4).
- Modules on the roadmap show an honest "not available yet" page rather than dead buttons.

## 8. Debugging tips
| Symptom | Cause / fix |
|---|---|
| Login spins, then "Can't reach the server" | API not running on :5078 (`dotnet run …`) |
| Signed out on every reload | the refresh cookie isn't reaching the API: use http://localhost:5173 (the proxy), not :5078 directly |
| A field error from the server doesn't show | the form must list the field in `applyFieldErrors(…, FIELDS)`; JSON keys are camelCase |
| Types out of date after a backend change | `npm run gen:api` |
| Test fails with an "unhandled frame" error | the test made a request with no MSW handler: add one with `server.use(...)` |
