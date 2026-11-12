using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Reports;

namespace StockPilot.Application.Features.Settings;

/// <summary>One changed field. Values are display strings; null means "empty" (or "not set").</summary>
public sealed record AuditChangeDto(string Field, string? Old, string? New);

public sealed record AuditLogDto(
    Guid Id, DateTimeOffset OccurredAt, string Action, string EntityType, string? EntityId,
    Guid? UserId, string? UserName, string? IpAddress, IReadOnlyList<AuditChangeDto> Changes);

public sealed record ListAuditLogsQuery : PageRequest
{
    public string? EntityType { get; init; }
    public string? Action { get; init; }
    public Guid? UserId { get; init; }

    /// <summary>Company-local dates (inclusive), like reports.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>
/// The company's audit trail, newest first (index: tenant_id, occurred_at). The tenant query filter on AuditLog means
/// a company only ever sees its own rows, and platform events (no tenant) never show up here.
/// </summary>
public sealed class ListAuditLogsHandler(IAppDbContext db, ReportClock clock)
{
    public async Task<PagedResult<AuditLogDto>> Handle(ListAuditLogsQuery query, CancellationToken ct)
    {
        var logs = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType)) logs = logs.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            // "Auth" matches every sign-in event (Auth.LoginSucceeded, Auth.LoginFailed, …).
            logs = query.Action == "Auth"
                ? logs.Where(a => a.Action.StartsWith("Auth."))
                : logs.Where(a => a.Action == query.Action);
        }
        if (query.UserId is { } userId) logs = logs.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            logs = logs.Where(a => a.EntityId != null && a.EntityId.Contains(term));
        }

        if (query.From is not null || query.To is not null)
        {
            var zone = await clock.TimeZoneAsync(ct);
            if (query.From is { } from)
            {
                var start = ReportClock.StartOf(from, zone);
                logs = logs.Where(a => a.OccurredAt >= start);
            }
            if (query.To is { } to)
            {
                var end = ReportClock.StartOf(to.AddDays(1), zone);
                logs = logs.Where(a => a.OccurredAt < end);
            }
        }

        // Count the log rows alone, then join names onto this page only (performance pass: counting through the users
        // join over 200k rows was most of the time).
        var total = await logs.CountAsync(ct);
        var items = await (
            from a in logs.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
                .Skip((query.SafePage - 1) * query.SafePageSize).Take(query.SafePageSize)
            join u in db.Users on a.UserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            orderby a.OccurredAt descending, a.Id descending
            select new { a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, a.UserId, UserName = u == null ? null : u.FirstName + " " + u.LastName, a.IpAddress, a.Changes }
        ).ToListAsync(ct);

        return new PagedResult<AuditLogDto>(
            items.Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, a.UserId, a.UserName, a.IpAddress, ParseChanges(a.Changes))).ToList(),
            query.SafePage, query.SafePageSize, total);
    }

    /// <summary>{"Name":{"old":"A","new":"B"}} → [Name: A → B]. Anything unexpected is skipped, never thrown.</summary>
    internal static IReadOnlyList<AuditChangeDto> ParseChanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            var changes = new List<AuditChangeDto>();
            foreach (var field in doc.RootElement.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Object) continue;
                changes.Add(new AuditChangeDto(
                    field.Name,
                    field.Value.TryGetProperty("old", out var o) ? Display(o) : null,
                    field.Value.TryGetProperty("new", out var n) ? Display(n) : null));
            }
            return changes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        _ => value.GetRawText(),
    };
}

/// <summary>Filter options: only entity types and people that actually appear in this company's log.</summary>
public sealed record AuditFiltersDto(IReadOnlyList<string> EntityTypes, IReadOnlyList<AuditUserOptionDto> Users);

public sealed record AuditUserOptionDto(Guid Id, string Name);

public sealed class GetAuditFiltersHandler(IAppDbContext db)
{
    public async Task<AuditFiltersDto> Handle(CancellationToken ct)
    {
        var types = await db.AuditLogs.AsNoTracking().Select(a => a.EntityType).Distinct().OrderBy(t => t).ToListAsync(ct);
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new AuditUserOptionDto(u.Id, u.FirstName + " " + u.LastName))
            .ToListAsync(ct);
        return new AuditFiltersDto(types, users);
    }
}
