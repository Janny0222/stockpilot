using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Features.Warehouses;

namespace StockPilot.Application.Features.Reports;

internal static class ReportScope
{
    /// <summary>
    /// Warehouses a report may include: null = all (unrestricted user, no filter). A requested warehouse the user
    /// can't access yields an EMPTY scope, so the report shows nothing rather than leaking that warehouse's numbers.
    /// </summary>
    public static async Task<IReadOnlyCollection<Guid>?> WarehousesAsync(WarehouseAccessService access, Guid? requested, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        if (requested is not { } only) return allowed;
        return allowed is null || allowed.Contains(only) ? [only] : [];
    }
}

/// <summary>
/// Turns "days" and calendar dates in the COMPANY's time zone into UTC instants for querying, and back.
/// "Today's sales" for a Manila company means Manila midnight to now, not UTC midnight (which is 8 a.m. there).
/// </summary>
public sealed class ReportClock(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<TimeZoneInfo> TimeZoneAsync(CancellationToken ct)
    {
        var id = await db.Tenants.Where(t => t.Id == currentUser.TenantId).Select(t => t.TimeZone).SingleOrDefaultAsync(ct);
        return Resolve(id);
    }

    public DateTimeOffset Now => clock.GetUtcNow();

    public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    /// <summary>Local midnight at the start of <paramref name="date"/>, as a UTC instant.</summary>
    public static DateTimeOffset StartOf(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>Unknown or unavailable zone ids fall back to UTC instead of failing the whole dashboard.</summary>
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
