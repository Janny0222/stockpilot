using StockPilot.Application.Features.Platform;

namespace StockPilot.UnitTests.Application;

/// <summary>"Daily at 19:00 UTC", with catch-up after the server slept through it, but only within the window.</summary>
public sealed class JobScheduleTests
{
    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static readonly JobSchedule Cleanup = new(19, TimeSpan.FromDays(1));
    private static readonly JobSchedule Digest = new(23, TimeSpan.FromHours(6));

    [Fact]
    public void Occurrences_are_today_or_yesterday_at_the_hour()
    {
        Cleanup.PreviousOccurrence(Utc(11, 20)).ShouldBe(Utc(11, 19));
        Cleanup.PreviousOccurrence(Utc(11, 18)).ShouldBe(Utc(10, 19));
        Cleanup.PreviousOccurrence(Utc(11, 19)).ShouldBe(Utc(11, 19)); // exactly on time counts as "now"
        Cleanup.NextOccurrence(Utc(11, 18)).ShouldBe(Utc(11, 19));
        Cleanup.NextOccurrence(Utc(11, 19)).ShouldBe(Utc(12, 19));
    }

    [Fact]
    public void Runs_once_per_occurrence()
    {
        Cleanup.IsDue(lastStartedAt: Utc(10, 19, 1), now: Utc(11, 19, 0)).ShouldBeTrue();  // new occurrence
        Cleanup.IsDue(lastStartedAt: Utc(11, 19, 1), now: Utc(11, 22, 0)).ShouldBeFalse(); // already ran for it
        Cleanup.IsDue(lastStartedAt: null, now: Utc(11, 20)).ShouldBeTrue();                // first deploy
    }

    [Fact]
    public void A_missed_run_is_caught_up_on_wake_but_only_within_the_window()
    {
        // The server slept from 18:00 to 02:00: the cleanup still runs on wake-up...
        Cleanup.IsDue(lastStartedAt: Utc(10, 19), now: Utc(12, 2)).ShouldBeTrue();

        // ...but a 07:00-Manila digest found at 13:00 Manila (6h late) is still sent, and at 14:00 it waits for tomorrow.
        Digest.IsDue(lastStartedAt: Utc(9, 23), now: Utc(11, 5)).ShouldBeTrue();
        Digest.IsDue(lastStartedAt: Utc(9, 23), now: Utc(11, 6, 1)).ShouldBeFalse();
    }
}
