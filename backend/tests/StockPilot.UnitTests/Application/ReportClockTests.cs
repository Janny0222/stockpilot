using StockPilot.Application.Features.Reports;

namespace StockPilot.UnitTests.Application;

public sealed class ReportClockTests
{
    // A fixed UTC+8 zone (like Asia/Manila) built in code, so the date math is tested the same on every OS.
    private static readonly TimeZoneInfo PlusEight = TimeZoneInfo.CreateCustomTimeZone("Test/UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    [Fact]
    public void A_local_day_starts_at_local_midnight_not_utc_midnight()
    {
        // UTC+8: Nov 9 starts at Nov 8, 16:00 UTC.
        ReportClock.StartOf(new DateOnly(2026, 11, 9), PlusEight).ShouldBe(new DateTimeOffset(2026, 11, 8, 16, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_late_evening_utc_sale_belongs_to_the_next_local_day()
    {
        // 17:30 UTC on Nov 9 is 01:30 on Nov 10 at UTC+8: it's part of Nov 10's sales.
        var instant = new DateTimeOffset(2026, 11, 9, 17, 30, 0, TimeSpan.Zero);
        ReportClock.LocalDate(instant, PlusEight).ShouldBe(new DateOnly(2026, 11, 10));
        ReportClock.Today(PlusEight, instant).ShouldBe(new DateOnly(2026, 11, 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public void Unknown_zones_fall_back_to_utc_instead_of_failing(string? id)
    {
        ReportClock.Resolve(id).ShouldBe(TimeZoneInfo.Utc);
    }

    /// <summary>
    /// On Linux (CI, and the Docker image, which copies /usr/share/zoneinfo) IANA ids resolve from tz files.
    /// Windows + InvariantGlobalization can't map IANA ids, so locally the app falls back to UTC: dev-only.
    /// </summary>
    [Fact]
    public void Iana_ids_resolve_on_linux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "IANA ids need tzdata (Linux); Windows dev runs in invariant mode.");
        ReportClock.Resolve("Asia/Manila").BaseUtcOffset.ShouldBe(TimeSpan.FromHours(8));
    }
}
