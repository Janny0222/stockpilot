using Microsoft.EntityFrameworkCore;

namespace StockPilot.Application.Common.Models;

/// <summary>Standard list envelope returned by every paged endpoint.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Common query-string parameters for lists. Values are clamped, never trusted.</summary>
public record PageRequest
{
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }

    /// <summary>"field:asc" or "field:desc". Unknown fields fall back to the default order.</summary>
    public string? Sort { get; init; }

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    public (string Field, bool Descending)? ParseSort()
    {
        if (string.IsNullOrWhiteSpace(Sort))
        {
            return null;
        }

        var parts = Sort.Split(':', 2, StringSplitOptions.TrimEntries);
        var descending = parts.Length == 2 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return (parts[0].ToLowerInvariant(), descending);
    }
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageRequest request, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.SafePage - 1) * request.SafePageSize)
            .Take(request.SafePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, request.SafePage, request.SafePageSize, total);
    }
}
