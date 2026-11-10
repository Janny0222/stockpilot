using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Application.Features.Reports;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
public sealed class DashboardController : ControllerBase
{
    /// <summary>Real aggregates only. Sections the caller's role can't see come back as null.</summary>
    [HttpGet]
    [HasPermission(Permissions.Dashboard.View)]
    public Task<DashboardDto> Get([FromQuery] DashboardQuery query, [FromServices] GetDashboardHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);
}

[ApiController]
[Route("api/v1/reports")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>inventory-valuation | low-stock | sales | purchases | stock-movements</summary>
    [HttpGet("{key}")]
    [HasPermission(Permissions.Reports.View)]
    public Task<ReportTableDto> Get(string key, [FromQuery] ReportQuery query, [FromServices] RunReportHandler handler, CancellationToken ct) =>
        handler.Handle(key, query, ct);

    [HttpGet("{key}/csv")]
    [HasPermission(Permissions.Reports.Export)]
    [Produces("text/csv")]
    public async Task<IActionResult> Csv(string key, [FromQuery] ReportQuery query, [FromServices] RunReportHandler handler, CancellationToken ct)
    {
        var report = await handler.Handle(key, query, ct);
        var name = $"{report.Key}-{(report.From is { } f ? $"{f:yyyyMMdd}-{report.To:yyyyMMdd}" : DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture))}.csv";
        // UTF-8 WITH a byte-order mark: Excel otherwise mis-reads names like "Casa Home Depot – Quezon City".
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportCsv.Write(report))).ToArray(), "text/csv; charset=utf-8", name);
    }
}

/// <summary>RFC 4180 CSV, with spreadsheet formula injection neutralised.</summary>
public static class ReportCsv
{
    public static string Write(ReportTableDto report)
    {
        var sb = new StringBuilder();
        Line(sb, report.Columns.Select(c => (object?)c.Label));
        foreach (var row in report.Rows)
        {
            Line(sb, row);
        }

        if (report.Totals is { } totals)
        {
            Line(sb, totals);
        }

        return sb.ToString();
    }

    private static void Line(StringBuilder sb, IEnumerable<object?> cells)
    {
        sb.AppendJoin(',', cells.Select(Cell));
        sb.Append("\r\n");
    }

    public static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        // A product named "=HYPERLINK(...)" must stay text when the file is opened in Excel/Sheets.
        // Numbers are written by us (never start with these) so only user-entered text is affected.
        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
