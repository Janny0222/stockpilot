# M11, step 2: Background jobs (in-process scheduler)

Three daily jobs run **inside the API**, with no Hangfire and no Redis.

| Job | When (UTC / Manila) | What it does |
|---|---|---|
| `token-cleanup` | 19:00 / 03:00 | Deletes refresh tokens and email links that expired more than a day ago |
| `notification-retention` | 19:00 / 03:00 | Deletes **read** notifications older than 90 days, and **any** older than a year |
| `low-stock-digest` | 23:00 / 07:00 | Emails people who opted in a list of everything at or below its reorder level |

## Why not Hangfire (or Redis)
The free tiers shaped this.
- **Neon's free compute suspends after ~5 idle minutes.** Hangfire's PostgreSQL storage polls its tables every few
  seconds, so Neon would never suspend and the monthly compute hours would run out.
- **The scheduler here touches the database only when a job is due:** once per scheduled moment, so about twice a day.
  The rest of the time it's a sleeping `Task.Delay`.
- **One Render instance:** there's nothing for Redis to share (no distributed cache, no SignalR backplane). When there
  are several instances, Redis makes sense for those two things, and the advisory lock below already keeps jobs from
  running twice.

What Hangfire would add (a dashboard, retries, history) is covered at this size by the **Background jobs** panel on
the platform page (last run, duration, result, next run, Run now) and the logs.

## How it works (`Infrastructure/Jobs/JobScheduling.cs`)
- **`JobScheduler` (a `BackgroundService`):**
  1. Waits 30 s after startup.
  2. Reads `job_runs` once and runs every job that's due, one at a time.
  3. Sleeps until the next scheduled moment, then repeats.
- **Catch-up after sleep:** the free Render instance sleeps after 15 idle minutes and nothing runs while it's asleep.
  The last run of each job is stored in the database, so on the next start a missed run is caught up, within the job's
  window:
  - Cleanups: any time that day.
  - The 07:00 digest: up to 6 hours late. After that it waits for tomorrow, because a "good morning" email at 9 PM is
    wrong. The rules are pinned by unit tests (`JobScheduleTests`).
- **`JobRunner`:** takes a **PostgreSQL advisory lock** named after the job, records start and finish (duration,
  result message), and never throws. A failed job is logged and recorded, and the app carries on.
  - **Why an advisory lock:** two instances, or the scheduler plus a "Run now" click, can't run the same job at once,
    and if the process dies mid-job, PostgreSQL drops the lock with the session. A "running" flag in a table would
    stay stuck.
  - **The lock connection is unpooled and unlocks explicitly.** Found by a flaky test: a pooled connection isn't
    really closed on Dispose, so its session (and the lock) lived on in the pool and blocked later runs.
- **Where the jobs live:** in `Application/Features/Platform` as plain scoped classes (`IBackgroundJob`). They have no
  signed-in tenant, so they span all companies on purpose. That folder is one the architecture test allows to bypass
  the tenant filters.

## Low-stock email
- **Opt-in:** Settings → Notifications → "Daily email with everything that needs reordering". Nobody gets mail they
  didn't ask for.
- **Recipients:** people whose role reorders (`purchases.manage`), who are active, and whose company isn't suspended.
- **Contents:** everything at or below its reorder level in the warehouses they can see, lowest first, up to 50 lines,
  with a link to the low-stock list.
- **Nothing low means no email.**
- **Preferences API:** each type now has `inApp` and `email`, and an update can send just one of them. `emailLabel`
  tells the UI which types have an email version (only LowStock today); asking for email on any other type gives a
  400.

## Configuration
| Setting | Default | |
|---|---|---|
| `Jobs__Enabled` | `true` | Integration tests turn it off and run jobs through the platform endpoint |
| `Jobs__StartupDelay` | `00:00:30` | |

**Optional, so the 07:00 digest isn't missed while the free server sleeps:**
- Point a free cron service (for example cron-job.org) at `https://<api>/health/live` around 22:55 UTC.
- That wakes the server, and the catch-up sends the digest. Without it, the digest goes out the first time someone
  opens the app before 13:00 Manila.

## Endpoints (Super Admin)
- `GET /api/v1/platform/jobs`: name, description, schedule, next run (or `dueNow`), and last run details
  (`platform.metrics.view`).
- `POST /api/v1/platform/jobs/{name}/run`: runs it now and returns the result (`platform.tenants.manage`).

## Tests
- **Unit (3):** occurrence math, once per occurrence, catch-up window.
- **Integration (5):**
  - token cleanup deletes only long-expired tokens, and the live session survives
  - retention keeps recent and unread notifications
  - the digest reaches only opted-in people with something low, and email on an unsupported type is a 400
  - a job already locked elsewhere is skipped
  - job status is platform-only, and an unknown job is a 404
- **Frontend (2):** the email switch sends just that switch, and the jobs panel shows status and runs a job.

Locally, the scheduler caught up all three jobs on its first start, didn't repeat them on a restart, and then slept
until 19:00 UTC. A "Run now" of the digest sent a real 28-line email (in the dev email log).
