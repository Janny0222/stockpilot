# Deployment: Neon + Render + Cloudflare Pages (free tier)

```
Browser ──► https://<project>.pages.dev            Cloudflare Pages (React build)
              └─ /api/*  ─► Pages Function proxy  ──►  https://<service>.onrender.com   Render (Docker, ASP.NET)
                                                          └─► Neon "production" branch   PostgreSQL
```

Do the steps **in this order**: each step produces a value the next one needs.

| Value | Where it comes from | Where it goes |
|---|---|---|
| Neon **pooled** connection string (production branch) | Neon → Connect, pooling ON | Render `ConnectionStrings__Database` |
| Neon **direct** connection string (production branch) | Neon → Connect, pooling OFF | your terminal, once, for migrations |
| JWT signing key | `openssl rand -base64 48` | Render `Jwt__SigningKey` |
| Proxy secret | `openssl rand -hex 32` | Render `ForwardedHeaders__ProxySecret` **and** Pages `PROXY_SECRET` |
| Render URL | Render, after the first deploy | Pages `API_ORIGIN` |
| Pages URL | Cloudflare, after the first deploy | Render `Auth__FrontendBaseUrl`, `Cors__AllowedOrigins__0` |

Run the `openssl` commands in **Git Bash**. Keep the values in a password manager, never in the repo.

---

## 1. Database: migrate the Neon production branch

Production does **not** migrate on startup (`Database:MigrateOnStartup=false`): schema changes are a deliberate step.

```bash
cd backend
dotnet tool restore
STOCKPILOT_DB='<direct production connection string>' \
  dotnet ef database update -p src/StockPilot.Infrastructure -s src/StockPilot.Infrastructure
```

`STOCKPILOT_DB` takes priority over your User Secret (the dev branch), so this targets production only.
Check in Neon → Tables (production branch) that `tenants`, `users`, `roles`… exist.

## 2. Backend: Render web service

Render → **New → Web Service** → connect the GitHub repo.

| Setting | Value |
|---|---|
| Name | `stockpilot-api` (becomes `stockpilot-api.onrender.com` if available) |
| Region | **Singapore** (same as Neon) |
| Branch | `main` |
| Runtime / Language | **Docker** |
| Dockerfile Path | `backend/Dockerfile` |
| Docker Build Context Directory | `.` (repository root) |
| Instance Type | **Free** |
| Health Check Path | `/health/live` (Advanced) |

Environment variables (Advanced → Environment Variables):

```
ASPNETCORE_ENVIRONMENT=Production
PORT=8080
ConnectionStrings__Database=<pooled production connection string>
Jwt__SigningKey=<openssl rand -base64 48>
ForwardedHeaders__Enabled=true
ForwardedHeaders__ProxySecret=<openssl rand -hex 32>
ForwardedHeaders__RequireProxySecret=true                  # only the Pages proxy may call the API (health checks excepted)
Auth__RequireConfirmedEmail=false
Auth__FrontendBaseUrl=https://placeholder.pages.dev        # fix in step 4
Cors__AllowedOrigins__0=https://placeholder.pages.dev      # fix in step 4
Platform__SuperAdmin__Email=<your admin email>
Platform__SuperAdmin__Password=<strong unique password>
Seed__DemoData=true
Seed__DemoPassword=<password for the public demo accounts>
Swagger__Enabled=true
```

Deploy. The first Docker build takes ~5–10 minutes. Then check:
`https://<service>.onrender.com/health/ready` → `Healthy`, and `/swagger` loads.

## 3. Frontend: Cloudflare Pages

Cloudflare dashboard → **Workers & Pages → Create → Pages → Connect to Git** → pick the repo.

| Setting | Value |
|---|---|
| Production branch | `main` |
| Framework preset | None (or Vite) |
| Root directory | `frontend` |
| Build command | `npm run build` |
| Build output directory | `dist` |

Environment variables (Settings → Variables and Secrets), for **Production**:

```
NODE_VERSION=24
API_ORIGIN=https://<service>.onrender.com      # no trailing slash
PROXY_SECRET=<same value as Render>            # type: Secret (encrypted)
```

`functions/api/[[path]].ts` is picked up automatically, because it lives inside the root directory.
No SPA redirect rule is needed: without a `404.html`, Pages serves `index.html` for unknown paths.

## 4. Point the API back at the frontend

Render → Environment: set `Auth__FrontendBaseUrl` and `Cors__AllowedOrigins__0` to the real
`https://<project>.pages.dev`, save (Render redeploys).

## 5. Smoke test

1. Open the Pages URL. The first visit may show **"Waking up the server…"** (Render free tier sleeps after 15 idle minutes).
2. Sign in as `owner@demo.stockpilot.app` with your `Seed__DemoPassword`.
3. Reload the page: you should stay signed in (refresh cookie through the proxy).
4. Warehouses: search and sort work. Sign out, then sign in as `inventory@demo.stockpilot.app`: only MNL is visible.
5. DevTools → Application → Cookies: `sp_rt` is HttpOnly, Secure, SameSite=Strict, Path `/api/v1/auth`, on the pages.dev host.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Render deploy fails at startup: "SigningKey must be at least 32 bytes" | `Jwt__SigningKey` missing or short |
| `/health/ready` unhealthy | connection string wrong, or Neon branch/database name mismatch |
| `relation "users" does not exist` in Render logs | step 1 (migrations) not run against **production** |
| Pages shows `PROXY_NOT_CONFIGURED` | `API_ORIGIN` not set for the Production environment (redeploy after setting) |
| Login works but a reload signs you out | calling Render directly instead of `/api` on the Pages domain |
| Everyone gets 429 on login | `PROXY_SECRET` and `ForwardedHeaders__ProxySecret` differ, so all users share Cloudflare's IP |
| Every API call returns 403 `DIRECT_ACCESS_BLOCKED` | `RequireProxySecret=true` but the Pages `PROXY_SECRET` is missing or different. Calling the Render URL directly is *meant* to be blocked |
| The API won't start: "ProxySecret must be at least 32 characters" | Use `openssl rand -hex 32` (64 characters). A short secret used to be silently ignored |
| Every deep link 404s on Pages | a `404.html` was added to `public/` (it disables SPA fallback) |

## Free-tier notes (verified 2026-11-09, re-check before relying on them)
- Render free web service: sleeps after 15 min idle, ~1 min wake-up, 750 instance-hours/month per workspace.
- Neon free: 100 CU-hours per project per month, autosuspends after 5 min idle.
- Cloudflare Pages: 500 builds/month; Pages Functions share the Workers free quota (100k requests/day).
