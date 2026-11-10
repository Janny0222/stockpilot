namespace StockPilot.Application.Common.Options;

/// <summary>Bound from the "Auth" configuration section. Defaults are production-safe.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Two browser tabs may refresh at the same moment with the same cookie. A token rotated less than
    /// this long ago is treated as a benign race (plain 401, client retries with the new cookie)
    /// instead of theft (whole family revoked).
    /// </summary>
    public TimeSpan RefreshTokenReuseGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan EmailVerificationTokenLifetime { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan PasswordResetTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan InvitationTokenLifetime { get; set; } = TimeSpan.FromDays(7);
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Set false only for a public demo deployment without an email provider.</summary>
    public bool RequireConfirmedEmail { get; set; } = true;

    /// <summary>Used to build links in emails, e.g. https://app.example.com/reset-password#token=…</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>How long a validated (user active, security stamp, tenant active) result is cached per token.</summary>
    public TimeSpan SessionValidationCacheDuration { get; set; } = TimeSpan.FromSeconds(60);
}
