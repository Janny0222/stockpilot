# StockPilot

Multi-tenant **inventory & warehouse management SaaS** for small and medium businesses.
ASP.NET Core 10 · Clean Architecture · EF Core + PostgreSQL · JWT with refresh-token rotation · React (coming in Phase 3).

> **Status:** Phase 4 in progress: **Products**, **Warehouses + Inventory**, **Suppliers + Purchasing** and
> **Customers + Sales** done: stock in/out, adjustments, transfers, movement ledger, row-locked concurrency (no
> overselling), idempotent writes, purchase orders with partial receiving, and sales orders that reserve stock on
> confirm and ship it out with cost of goods and margin.
> Phase 3 (frontend foundation) complete. Backend: authentication, tenant isolation, roles and
> permissions, auditing, logging and API docs (102 tests). Frontend: design system, responsive app shell, every auth
> flow, role-based navigation, and the Warehouses and Account pages on the real API (26 tests).
> See [docs/](docs/) for the plan and design notes.

---

## Repository layout

```
backend/
  src/
    StockPilot.Domain/          entities, business rules: zero dependencies
    StockPilot.Application/     use cases (handlers), DTOs, validators, ports (interfaces)
    StockPilot.Infrastructure/  EF Core, PostgreSQL, JWT, password hashing, email, seeding
    StockPilot.API/             controllers, middleware, auth wiring, Program.cs
  tests/
    StockPilot.UnitTests/         fast, no database (domain, validators, security primitives, architecture rules)
    StockPilot.IntegrationTests/  real HTTP pipeline + real PostgreSQL
docs/                           planning, ERD, architecture notes
scripts/                        dev helpers
docker-compose.yml              postgres + mailpit + api
```

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.1xx+ | pinned via `backend/global.json` |
| PostgreSQL | 17 | a **Neon dev branch** (no install), Docker (`docker compose up -d postgres`), **or** any local server on port **5433** |
| Docker Desktop | optional | needed for `docker compose` and for integration tests without `STOCKPILOT_TEST_DB` |

## Run the API locally

### 1. Pick a database

**Option A: Neon dev branch (nothing to install).**
In the Neon console create a branch named `dev` (keep `main`/`production` for production), open **Connect**,
choose **.NET**, turn **Connection pooling off** (migrations need a direct connection), copy the snippet, then:

```bash
cd backend
# Single quotes so Bash leaves any "$" in the password alone. Never commit this value.
dotnet user-secrets set 'ConnectionStrings:Database' '<paste the .NET connection string>' --project src/StockPilot.API
```

User Secrets live outside the repo (`%APPDATA%\Microsoft\UserSecrets\`) and override `appsettings.Development.json`.
`dotnet ef` reads the same secret.

**Option B: local PostgreSQL on port 5433.**

```bash
docker compose up -d postgres mailpit
```

### 2. Run (applies migrations and seeds demo data automatically in Development)

```bash
cd backend
dotnet run --project src/StockPilot.API --launch-profile http
```

On a brand-new database the first run logs one `ERR … __EFMigrationsHistory` line: EF checks its history table
before creating it. That's expected and harmless.

Open **http://localhost:5078/swagger**.

### Demo accounts (Development only)

| Role | Email | Password |
|---|---|---|
| Owner | owner@demo.stockpilot.app | `Demo@12345` |
| Manager | manager@demo.stockpilot.app | `Demo@12345` |
| Inventory Staff (MNL warehouse only) | inventory@demo.stockpilot.app | `Demo@12345` |
| Sales Staff | sales@demo.stockpilot.app | `Demo@12345` |
| Platform Super Admin | admin@stockpilot.local | `Admin@12345` |

These come from `appsettings.Development.json`, which is safe because the app **refuses to start outside
Development** with the development JWT key, and production seeds nothing without explicit env vars.

In Swagger: call `POST /api/v1/auth/login`, copy `accessToken`, click **Authorize**, paste it.

Emails (verification, password reset): with `Email:Provider=Log` (default in Development) they are printed to the
console; with Docker's Mailpit they appear at **http://localhost:8025**.

## Run the frontend

Needs Node 24 (see `frontend/.nvmrc`) and the API running on port 5078.

```bash
cd frontend
npm install
npm run dev
```

Open **http://localhost:5173**. In development the login page has one-click **Demo accounts** buttons.
Vite proxies `/api` to the API, so the browser sees a single origin (the HttpOnly refresh cookie works the same way
it will in production behind the Cloudflare Pages proxy).

| Script | What it does |
|---|---|
| `npm run dev` | dev server with hot reload |
| `npm test` | Vitest + Testing Library + MSW |
| `npm run typecheck` / `npm run lint` | TypeScript and ESLint |
| `npm run build` | production bundle in `dist/` |
| `npm run gen:api` | regenerate `src/types/api.gen.ts` from the running API's OpenAPI document |

## Tests

```bash
cd backend
dotnet test --project tests/StockPilot.UnitTests

# Integration tests need PostgreSQL. Either have Docker running (Testcontainers starts one), or point at a server:
$env:STOCKPILOT_TEST_DB = "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=postgres"   # PowerShell
dotnet test --project tests/StockPilot.IntegrationTests
```

Each integration-test run creates a throwaway database and drops it afterwards.

## Database migrations

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> -p src/StockPilot.Infrastructure -s src/StockPilot.Infrastructure -o Persistence/Migrations
dotnet ef database update      -p src/StockPilot.Infrastructure -s src/StockPilot.Infrastructure
```

`database update` targets, in order: the `STOCKPILOT_DB` env var, then the API's User Secrets connection string,
then `localhost:5433`. CI fails if the model changed without a migration (`dotnet ef migrations has-pending-model-changes`).

## Configuration

All settings are standard ASP.NET Core configuration (`appsettings.json` → environment variables, with `__` for `:`).
Production variables are documented in [`.env.example`](.env.example). **No secrets are committed.**

## API overview (Phase 2)

| Method | Path | Auth |
|---|---|---|
| POST | `/api/v1/auth/register` | anonymous, rate-limited |
| POST | `/api/v1/auth/login` | anonymous, rate-limited |
| POST | `/api/v1/auth/refresh` | refresh cookie |
| POST | `/api/v1/auth/logout` · `/logout-all` | cookie · bearer |
| POST | `/api/v1/auth/verify-email` · `/resend-verification` | anonymous |
| POST | `/api/v1/auth/forgot-password` · `/reset-password` | anonymous |
| GET | `/api/v1/auth/me` · `/sessions` · DELETE `/sessions/{id}` | bearer |
| GET | `/api/v1/warehouses` · `/warehouses/{id}` | `warehouses.view` |
| GET | `/health/live` · `/health/ready` · `/openapi/v1.json` · `/swagger` | anonymous |

Errors are [RFC 9457 ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) with a stable `code` and a `traceId`.
