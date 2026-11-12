namespace StockPilot.Domain.Platform;

/// <summary>
/// The last run of one background job (one row per job, keyed by name). Kept in the database rather than in memory so a
/// restart (the free Render instance sleeps after 15 idle minutes) knows what it missed and catches up.
/// </summary>
public sealed class JobRun
{
    public const int MessageMaxLength = 500;

    private JobRun() { }

    public JobRun(string name) => Name = name;

    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastFinishedAt { get; private set; }
    public bool? LastSucceeded { get; private set; }

    /// <summary>What the run did ("Deleted 12 expired tokens") or why it failed.</summary>
    public string? LastMessage { get; private set; }
    public int? LastDurationMs { get; private set; }

    public void Started(DateTimeOffset at) => LastStartedAt = at;

    public void Finished(DateTimeOffset at, bool succeeded, string? message)
    {
        LastFinishedAt = at;
        LastSucceeded = succeeded;
        LastMessage = message is { Length: > MessageMaxLength } ? message[..(MessageMaxLength - 1)] + "…" : message;
        LastDurationMs = LastStartedAt is { } started ? (int)Math.Max(0, (at - started).TotalMilliseconds) : null;
    }
}
