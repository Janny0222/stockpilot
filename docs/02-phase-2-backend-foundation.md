# Phase 2: Backend Foundation (how it works and why)

A walkthrough of what was built in Phase 2 and the reasoning behind it. Read it alongside the code;
file references are clickable in most editors.

---

## 1. What a request goes through

```
HTTP request
 │
 ├─ UseForwardedHeaders     (prod only) real client IP from Render/Cloudflare
 ├─ UseExceptionHandler     anything thrown below becomes ProblemDetails (GlobalExceptionHandler)
 ├─ UseSerilogRequestLogging  one structured line: method, path, status, elapsed ms
 ├─ UseCors
 ├─ UseAuthentication       JwtBearer validates signature, issuer, audience, expiry,
 │                          THEN OnTokenValidated asks ISessionValidator: user still active?
 │                          security stamp unchanged? company not suspended?
 ├─ UseRateLimiter          "auth" policy per IP on login/register; global per user
 ├─ UseAuthorization        fallback policy = must be authenticated;
 │                          [HasPermission(x)] → PermissionAuthorizationHandler → role's permission set
 └─ Controller              calls ONE handler; no business logic
      └─ Handler (Application)   validate → load via IAppDbContext (tenant-filtered) → domain methods → SaveChanges
           └─ SaveChanges interceptors: TenantIsolationInterceptor (block cross-tenant writes)
                                        AuditingInterceptor (stamp Created/Updated, write audit_log rows)
```

**Why middleware order matters:** each `Use…` wraps everything after it. The exception handler is near the top so it
can catch errors from authentication and controllers alike. Rate limiting comes after authentication so the global
limiter can partition by user ID instead of IP.

## 2. Clean Architecture in practice

| Project | May reference | Example |
|---|---|---|
| Domain | nothing | `User.RecordFailedLogin()` implements lockout as a pure rule, unit-tested without a DB |
| Application | Domain, EF Core *abstractions* | `LoginHandler` orchestrates; it doesn't know about HTTP, Npgsql or JWT libraries |
| Infrastructure | Application | `JwtAccessTokenService`, `AppDbContext`, `SmtpEmailSender` implement Application's interfaces |
| API | Infrastructure | wires DI, middleware, controllers |

`ArchitectureTests` enforces this. If someone adds `using Npgsql` to Application, CI fails.

**Pragmatic choices (on purpose):**
- **No repository-per-entity.** `DbContext` already is a unit of work, and `DbSet<T>` already is a repository.
  Handlers use `IAppDbContext`.
- **No MediatR.** Controllers inject the concrete handler (`[FromServices] LoginHandler`). It's explicit,
  "Go to definition" works, and there's no licence question.
- **Exceptions for failures.** Handlers `throw new NotFoundException(...)`, and one place
  (`GlobalExceptionHandler`) maps types to status codes. This keeps happy-path code readable.

## 3. Authentication

### Tokens
| | Access token | Refresh token |
|---|---|---|
| Format | JWT (HS256), signed | 256-bit random string |
| Lifetime | 15 min | 7 days |
| Stored by the client | in memory (Zustand) | `HttpOnly; Secure; SameSite=Strict` cookie, path `/api/v1/auth` |
| Stored by the server | not stored | **SHA-256 hash** only |
| Contains | `sub`, `tid`, `rid`, `sstamp`, `jti` | nothing (opaque) |

**Why two tokens?** A stolen access token is useful for at most 15 minutes. The long-lived refresh token can't be
read by JavaScript (HttpOnly), so an XSS bug can't exfiltrate it.

### Rotation + reuse detection (`RefreshSessionHandler`)
1. Client calls `/auth/refresh`; the browser attaches the cookie automatically.
2. Server finds the token by hash. If it's valid, the server revokes it (`Rotated`), issues a new one in the **same family**,
   and sets the new cookie.
3. If a **revoked** token is presented again, either an attacker replayed a stolen copy or the client misbehaved. Either way,
   the server revokes the **whole family** and the user must sign in again. Test: `Refresh_rotates_the_token_and_reusing_an_old_one_revokes_the_whole_family`.
4. Two tabs refreshing at the same instant are a real-world race. A configurable grace window (30 s) plus the `xmin`
   concurrency token turn that into a harmless 401 instead of a forced logout.

### Instant revocation with a security stamp
JWTs can't be "un-issued". Every token therefore carries the user's `SecurityStamp`, and every request compares it
with the database (cached for 60 s). Changing the password, "log out everywhere" or deactivating the user rotates the
stamp, so old tokens die within the cache window (the tests set the cache to 0 to prove it).

### Anti-enumeration
- Login returns the same message for "no such email" and "wrong password", and it spends the same hashing time
  (a dummy hash is verified).
- Forgot-password and resend-verification always return `202 Accepted`.
- Account state (disabled, suspended, unverified) is only revealed **after** the correct password is supplied.

## 4. Tenant isolation (the core security property)

Three layers. Each one alone would stop the common bugs; together they make a leak very unlikely.

1. **Tenant comes only from the validated JWT** (`HttpContextCurrentUser.TenantId`). It's never taken from a header,
   route or body, so spoofing it means forging a signature.
2. **Global query filter on every `ITenantOwned` entity** (`AppDbContext.ApplyTenantQueryFilters`). EF adds
   `WHERE tenant_id = @current` to every query automatically. With no tenant (anonymous, platform admin), the filter
   matches nothing, so the default is deny.
3. **Write guard** (`TenantIsolationInterceptor`). Even if a bug loads another tenant's row (e.g. via
   `IgnoreQueryFilters`), saving a change to it throws.

Plus:
- **404, not 403**, for another tenant's IDs, so attackers can't confirm an ID exists.
- **UUID v7 IDs** are not guessable or sequential.
- `IgnoreQueryFilters()` is allowed only in an approved list of files (architecture test).
- **Proof the tests work:** a mutation test disabled layers 2 and 3, and 7 of the 9 isolation tests failed.

## 5. Database transactions: the short version

`SaveChanges()` wraps **all** pending inserts and updates in **one** database transaction. Registration adds a tenant,
an owner, a warehouse, a verification token **and** their audit rows, then calls `SaveChanges` once. Either every row
commits or none does. You never get a company without an owner.

In Phase 4 (inventory) we'll need more: explicit transactions with row locks (`SELECT … FOR UPDATE`) so two
simultaneous stock-outs can't oversell. The `EnableRetryOnFailure` execution strategy configured here will then wrap
those transactions so a transient Neon disconnect retries the *whole* unit of work.

## 6. Debugging guide

| Symptom | Likely cause | Fix |
|---|---|---|
| App exits on start: *"Jwt:SigningKey must be at least 32 bytes"* | no key configured | set `Jwt__SigningKey` (prod) or run with `ASPNETCORE_ENVIRONMENT=Development` |
| *"development JWT signing key must not be used outside Development"* | dev key in prod | generate a real key: `openssl rand -base64 48` |
| `Failed to connect to 127.0.0.1:5433` | DB not running | `docker compose up -d postgres`, or check the Neon connection string in User Secrets |
| 401 on every call in Swagger | token expired (15 min) or not "Bearer"-prefixed | log in again; Swagger adds "Bearer" for you |
| 403 with a valid token | missing permission | check `GET /auth/me` → `permissions` |
| 404 on a record you know exists | it belongs to another tenant (by design) or your warehouse scope excludes it | log in as that tenant's user |
| `relation "xyz" does not exist` | migrations not applied | `dotnet ef database update …` or set `Database:MigrateOnStartup=true` |
| Any 500 | see the console log line with the matching `traceId` from the response body | |

Find a request's log line: copy `traceId` from the error JSON and search the console output for it.

## 7. Deferred to later phases (by design)
- User/role management endpoints (Settings module, Phase 4).
- Platform tenant management endpoints (Phase 4). The `platform.*` permissions and the Super Admin account already exist.
- Redis cache, Hangfire, SignalR (Phase 5).
- Same-origin refresh through the Cloudflare Pages Function proxy (deployment, Phase 6).
