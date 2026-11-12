using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Features.Platform;
using StockPilot.Domain.Platform;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Off in integration tests (they trigger jobs explicitly) or wherever another process runs them.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Let the app finish starting (migrations, warm-up) before the first catch-up check.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Runs one job: takes a PostgreSQL advisory lock named after the job (so two instances, or the scheduler and a
/// "Run now" click, never run it twice at once), records start and finish in job_runs, and never throws: a failed job
/// is logged and recorded, and the app carries on.
///
/// Why an advisory lock instead of a "running" flag in a table: the lock belongs to the connection, so if the process
/// dies mid-job PostgreSQL releases it by itself. A flag would stay stuck at "running" forever.
/// </summary>
internal sealed class JobRunner(IServiceScopeFactory scopes, TimeProvider clock, ILogger<JobRunner> logger) : IJobRunner
{
    public async Task<JobRunOutcome> RunAsync(string name, CancellationToken cancellationToken)
    {
        var definition = PlatformJobs.Find(name) ?? throw new ArgumentException($"Unknown job '{name}'.", nameof(name));

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A dedicated connection holds the lock for the whole run, independent of the job's own queries. Pooling is OFF
        // for it: a pooled connection isn't really closed on Dispose, so its session (and the lock) would live on in the
        // pool and block every later run. Without pooling, closing it ends the session and PostgreSQL drops the lock.
        var lockConnectionString = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false }.ConnectionString;
        await using var lockConnection = new NpgsqlConnection(lockConnectionString);
        await lockConnection.OpenAsync(cancellationToken);
        var key = JobLocks.KeyFor(name);
        await using (var take = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", lockConnection))
        {
            take.Parameters.AddWithValue("key", key);
            if (await take.ExecuteScalarAsync(cancellationToken) is not true)
            {
                return new JobRunOutcome(name, Ran: false, Succeeded: null, "Already running somewhere else; skipped.");
            }
        }

        try
        {
            return await RunLockedAsync(name, definition, scope.ServiceProvider, db, cancellationToken);
        }
        finally
        {
            // Release explicitly as well, so the lock is gone the moment the run ends, not when the socket closes.
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", lockConnection);
            release.Parameters.AddWithValue("key", key);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    private async Task<JobRunOutcome> RunLockedAsync(
        string name, JobDefinition definition, IServiceProvider services, AppDbContext db, CancellationToken cancellationToken)
    {
        var run = await db.JobRuns.SingleOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (run is null)
        {
            run = new JobRun(name);
            db.JobRuns.Add(run);
        }

        run.Started(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        bool succeeded;
        string message;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var job = (IBackgroundJob)services.GetRequiredService(definition.JobType);
            message = await job.RunAsync(cancellationToken);
            succeeded = true;
            logger.LogInformation("Job {Job} finished in {Elapsed} ms: {Message}", name, stopwatch.ElapsedMilliseconds, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            succeeded = false;
            message = $"Failed: {ex.Message}";
            logger.LogError(ex, "Job {Job} failed after {Elapsed} ms", name, stopwatch.ElapsedMilliseconds);
        }

        db.ChangeTracker.Clear(); // the job may have left tracked entities; record the result on a clean slate
        var record = await db.JobRuns.SingleAsync(r => r.Name == name, CancellationToken.None);
        record.Finished(clock.GetUtcNow(), succeeded, message);
        await db.SaveChangesAsync(CancellationToken.None);
        return new JobRunOutcome(name, Ran: true, succeeded, message);
    }
}

/// <summary>
/// The in-process scheduler. It sleeps until the next scheduled moment, then reads job_runs ONCE and runs what's due.
/// Between runs it doesn't touch the database at all, so Neon's free compute can still suspend when nobody's using
/// the app (a poller like Hangfire's would keep it awake around the clock).
///
/// On start it does the same check, which is how missed runs are caught up after the free Render instance wakes up.
/// </summary>
internal sealed class JobScheduler(IJobRunner runner, IServiceScopeFactory scopes, TimeProvider clock, IOptions<JobsOptions> options, ILogger<JobScheduler> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Background jobs are disabled (Jobs:Enabled=false)");
            return;
        }

        try
        {
            await Task.Delay(options.Value.StartupDelay, clock, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunDueJobsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Database unreachable (Neon waking up, network blip): try again shortly instead of waiting a day.
                    logger.LogWarning(ex, "Checking for due jobs failed; retrying in 5 minutes");
                    await Task.Delay(TimeSpan.FromMinutes(5), clock, stoppingToken);
                    continue;
                }

                var now = clock.GetUtcNow();
                var next = PlatformJobs.All.Min(j => j.Schedule.NextOccurrence(now));
                logger.LogInformation("Next background job check at {Next:u}", next);
                await Task.Delay(next - now + TimeSpan.FromSeconds(1), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task RunDueJobsAsync(CancellationToken ct)
    {
        Dictionary<string, DateTimeOffset?> lastStarted;
        await using (var scope = scopes.CreateAsyncScope())
        {
            lastStarted = await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobRuns.AsNoTracking()
                .ToDictionaryAsync(r => r.Name, r => r.LastStartedAt, ct);
        }

        var now = clock.GetUtcNow();
        foreach (var job in PlatformJobs.All.Where(j => j.Schedule.IsDue(lastStarted.GetValueOrDefault(j.Name), now)))
        {
            await runner.RunAsync(job.Name, ct); // one at a time: they're small, and it keeps the load gentle
        }
    }
}

/// <summary>Advisory-lock keys for jobs.</summary>
public static class JobLocks
{
    /// <summary>Stable 64-bit key per job name (FNV-1a). string.GetHashCode is randomized per process, so it won't do.</summary>
    public static long KeyFor(string jobName)
    {
        const ulong offset = 14695981039346656037, prime = 1099511628211;
        var hash = offset;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes("stockpilot-job:" + jobName))
        {
            hash = (hash ^ b) * prime;
        }

        return unchecked((long)hash);
    }
}
