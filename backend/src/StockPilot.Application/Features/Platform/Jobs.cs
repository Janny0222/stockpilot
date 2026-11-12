using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Features.Notifications;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Notifications;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Application.Features.Platform;

/// <summary>
/// "Once a day at HH:00 UTC". If the moment passes while the server is asleep (free Render instances sleep after 15
/// idle minutes), the job still runs when the server wakes, but only within the catch-up window: a cleanup can run
/// late, while a 7 AM digest arriving at 9 PM would be wrong, so it waits for tomorrow instead.
/// </summary>
public sealed record JobSchedule(int HourUtc, TimeSpan CatchUpWindow)
{
    /// <summary>The most recent scheduled moment at or before <paramref name="now"/>.</summary>
    public DateTimeOffset PreviousOccurrence(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date.AddHours(HourUtc), TimeSpan.Zero);
        return today <= now ? today : today.AddDays(-1);
    }

    public DateTimeOffset NextOccurrence(DateTimeOffset now) => PreviousOccurrence(now).AddDays(1);

    /// <summary>Not yet run since the latest occurrence, and that occurrence is recent enough to still be worth it.</summary>
    public bool IsDue(DateTimeOffset? lastStartedAt, DateTimeOffset now)
    {
        var previous = PreviousOccurrence(now);
        return (lastStartedAt is null || lastStartedAt < previous) && now - previous <= CatchUpWindow;
    }

    public string Describe() => $"Daily at {HourUtc:00}:00 UTC";
}

/// <summary>A background job: plain scoped class, so it gets IAppDbContext etc. like any use case.</summary>
public interface IBackgroundJob
{
    /// <summary>Runs once. Returns a one-line summary for the job status ("Deleted 12 expired tokens").</summary>
    Task<string> RunAsync(CancellationToken ct);
}

public sealed record JobDefinition(string Name, string Description, JobSchedule Schedule, Type JobType);

/// <summary>The catalogue: what runs, when, and why.</summary>
public static class PlatformJobs
{
    private static readonly TimeSpan Anytime = TimeSpan.FromDays(1);

    public static JobDefinition TokenCleanup { get; } = new(
        "token-cleanup", "Deletes sign-in and email-link tokens that expired more than a day ago.",
        new JobSchedule(19, Anytime), typeof(TokenCleanupJob)); // 03:00 in Manila

    public static JobDefinition NotificationRetention { get; } = new(
        "notification-retention", "Deletes read notifications older than 90 days, and any older than a year.",
        new JobSchedule(19, Anytime), typeof(NotificationRetentionJob));

    public static JobDefinition LowStockDigest { get; } = new(
        "low-stock-digest", "Emails the people who opted in a daily list of items at or below their reorder level.",
        new JobSchedule(23, TimeSpan.FromHours(6)), typeof(LowStockDigestJob)); // 07:00 in Manila

    public static IReadOnlyList<JobDefinition> All { get; } = [TokenCleanup, NotificationRetention, LowStockDigest];

    public static JobDefinition? Find(string name) => All.FirstOrDefault(j => j.Name == name);
}

/// <summary>
/// Expired tokens are useless (an expired refresh token is rejected whatever its state), so keeping them only grows
/// the table. The one-day margin keeps a just-expired token around for reuse detection in the meantime.
/// </summary>
public sealed class TokenCleanupJob(IAppDbContext db, TimeProvider clock) : IBackgroundJob
{
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-1);
        var refresh = await db.RefreshTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
        var links = await db.UserTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
        return $"Deleted {refresh} expired sign-in tokens and {links} expired email links.";
    }
}

/// <summary>The bell shows the latest notifications; nobody scrolls back a year. Unread ones get longer.</summary>
public sealed class NotificationRetentionJob(IAppDbContext db, TimeProvider clock) : IBackgroundJob
{
    public const int ReadDays = 90;
    public const int AnyDays = 365;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var readCutoff = now.AddDays(-ReadDays);
        var anyCutoff = now.AddDays(-AnyDays);
        // Platform job: no signed-in tenant, so it spans every company on purpose.
        var deleted = await db.Notifications.IgnoreQueryFilters()
            .Where(n => (n.ReadAt != null && n.CreatedAt < readCutoff) || n.CreatedAt < anyCutoff)
            .ExecuteDeleteAsync(ct);
        return $"Deleted {deleted} old notifications.";
    }
}

/// <summary>
/// One email per person who turned on "Daily email" for low stock: everything at or below its reorder level in the
/// warehouses they can see. Nothing low → no email. Suspended companies and deactivated people get nothing.
/// </summary>
public sealed class LowStockDigestJob(IAppDbContext db, IEmailSender email, IOptions<AuthOptions> options) : IBackgroundJob
{
    public const int MaxItems = 50;

    private sealed record Line(string Sku, string Product, string? Variant, string Warehouse, decimal Available, decimal ReorderLevel);

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var permission = NotificationCatalog.Get(NotificationType.LowStock).Permission;
        var recipients = await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId != null && u.IsActive && u.PasswordHash != null)
            .Where(u => db.Tenants.Any(t => t.Id == u.TenantId && t.Status == TenantStatus.Active))
            .Where(u => db.RolePermissions.Any(rp => rp.RoleId == u.RoleId && rp.PermissionCode == permission))
            .Where(u => db.NotificationPreferences.IgnoreQueryFilters().Any(p => p.UserId == u.Id && p.Type == NotificationType.LowStock && p.Email))
            .Select(u => new
            {
                u.Id, u.Email, u.FirstName, TenantId = u.TenantId!.Value, u.Role.RestrictToAssignedWarehouses,
                Company = db.Tenants.Where(t => t.Id == u.TenantId).Select(t => t.Name).Single(),
            })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var r in recipients)
        {
            var allowed = r.RestrictToAssignedWarehouses
                ? await db.UserWarehouseAccess.IgnoreQueryFilters().Where(a => a.UserId == r.Id).Select(a => a.WarehouseId).ToListAsync(ct)
                : null;

            var query =
                from s in db.WarehouseStocks.IgnoreQueryFilters()
                join w in db.Warehouses.IgnoreQueryFilters() on s.WarehouseId equals w.Id
                join v in db.ProductVariants.IgnoreQueryFilters() on s.ProductVariantId equals v.Id
                join p in db.Products.IgnoreQueryFilters() on v.ProductId equals p.Id
                where s.TenantId == r.TenantId && w.IsActive && v.IsActive && p.Status == Domain.Catalog.ProductStatus.Active
                      && s.QuantityOnHand - s.QuantityReserved <= v.ReorderLevel
                      && (allowed == null || allowed.Contains(w.Id))
                orderby s.QuantityOnHand - s.QuantityReserved, p.Name
                select new Line(v.Sku, p.Name, v.Name, w.Name, s.QuantityOnHand - s.QuantityReserved, v.ReorderLevel);

            var total = await query.CountAsync(ct);
            if (total == 0) continue;
            var lines = await query.Take(MaxItems).ToListAsync(ct);

            await email.SendAsync(Compose(r.Email, r.FirstName, r.Company, lines, total), ct);
            sent++;
        }

        return $"Sent {sent} digest email{(sent == 1 ? "" : "s")} ({recipients.Count} opted in).";
    }

    private EmailMessage Compose(string to, string firstName, string company, IReadOnlyList<Line> lines, int total)
    {
        var url = $"{options.Value.FrontendBaseUrl.TrimEnd('/')}/inventory?low=1";
        var outCount = lines.Count(l => l.Available <= 0);
        var subject = $"{total} {(total == 1 ? "item needs" : "items need")} reordering at {company}";
        var more = total > lines.Count ? $"…and {total - lines.Count} more." : null;
        static string H(string s) => WebUtility.HtmlEncode(s);
        static string Name(Line l) => l.Variant is null ? l.Product : $"{l.Product} ({l.Variant})";

        var html = new StringBuilder($"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:640px;margin:auto;color:#0f172a">
              <h2 style="color:#4f46e5">StockPilot</h2>
              <p>Good morning {H(firstName)},</p>
              <p>{total} {(total == 1 ? "item is" : "items are")} at or below the reorder level at {H(company)}{(outCount > 0 ? $", {outCount} of them out of stock" : "")}.</p>
              <table style="border-collapse:collapse;width:100%;font-size:14px">
                <tr style="text-align:left;color:#64748b"><th style="padding:6px">Item</th><th style="padding:6px">Warehouse</th><th style="padding:6px;text-align:right">Available</th><th style="padding:6px;text-align:right">Reorder at</th></tr>
            """);
        foreach (var l in lines)
        {
            html.Append($"""<tr style="border-top:1px solid #e2e8f0"><td style="padding:6px">{H(Name(l))}<br><span style="color:#64748b">{H(l.Sku)}</span></td><td style="padding:6px">{H(l.Warehouse)}</td><td style="padding:6px;text-align:right;{(l.Available <= 0 ? "color:#dc2626;font-weight:600" : "")}">{l.Available:0.###}</td><td style="padding:6px;text-align:right">{l.ReorderLevel:0.###}</td></tr>""");
        }

        html.Append($"""
              </table>
              {(more is null ? "" : $"<p style=\"color:#64748b\">{more}</p>")}
              <p><a href="{H(url)}" style="display:inline-block;background:#4f46e5;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none">Open low stock</a></p>
              <p style="color:#64748b;font-size:13px">You get this because you turned on the daily low-stock email. Turn it off in Settings, Notifications.</p>
            </div>
            """);

        // A colon, not a period: company names often end with one already ("Northwind Supply Co.").
        var text = new StringBuilder($"Good morning {firstName},\n\n{subject}:\n\n");
        foreach (var l in lines) text.Append($"- {Name(l)} ({l.Sku}), {l.Warehouse}: {l.Available:0.###} available, reorder at {l.ReorderLevel:0.###}\n");
        if (more is not null) text.Append(more).Append('\n');
        text.Append($"\nOpen low stock: {url}\n\nTurn this email off in Settings, Notifications.");

        return new EmailMessage(to, subject, html.ToString(), text.ToString());
    }
}

public sealed record JobStatusDto(
    string Name, string Description, string Schedule, DateTimeOffset NextRunAt, bool DueNow,
    DateTimeOffset? LastStartedAt, DateTimeOffset? LastFinishedAt, bool? LastSucceeded, string? LastMessage, int? LastDurationMs);

public sealed class ListJobsHandler(IAppDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<JobStatusDto>> Handle(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var runs = await db.JobRuns.AsNoTracking().ToDictionaryAsync(r => r.Name, ct);
        return PlatformJobs.All.Select(j =>
        {
            runs.TryGetValue(j.Name, out var r);
            // Due now (missed while asleep) → it runs at the next wake-up; otherwise at the next scheduled moment.
            var due = j.Schedule.IsDue(r?.LastStartedAt, now);
            var next = due ? now : j.Schedule.NextOccurrence(now);
            return new JobStatusDto(j.Name, j.Description, j.Schedule.Describe(), next, due,
                r?.LastStartedAt, r?.LastFinishedAt, r?.LastSucceeded, r?.LastMessage, r?.LastDurationMs);
        }).ToList();
    }
}

/// <summary>"Run now" for the Super Admin: same runner, same lock and record as the scheduler.</summary>
public sealed class RunJobHandler(IJobRunner runner)
{
    public Task<JobRunOutcome> Handle(string name, CancellationToken ct) =>
        PlatformJobs.Find(name) is null ? throw new Common.Exceptions.NotFoundException("Job", name) : runner.RunAsync(name, ct);
}
