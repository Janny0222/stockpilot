using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth.Common;

/// <summary>
/// Builds and sends auth emails. Tokens go in the URL *fragment* (#token=…), not the query string:
/// fragments are never sent to servers, so the token cannot leak into proxy/CDN access logs or
/// Referer headers. The SPA reads it from window.location.hash.
///
/// Sending failures are logged, not thrown: the database work already committed, and the user
/// can request a new email ("resend verification" / "forgot password" again).
/// </summary>
public sealed class AuthEmailService(
    IEmailSender sender,
    IOptions<AuthOptions> options,
    ILogger<AuthEmailService> logger)
{
    public Task SendEmailVerificationAsync(User user, string rawToken, CancellationToken ct) =>
        SendAsync(
            user,
            subject: "Verify your StockPilot email",
            intro: "Welcome to StockPilot! Please confirm your email address to activate your account.",
            actionText: "Verify email",
            url: $"{BaseUrl}/verify-email#token={Uri.EscapeDataString(rawToken)}",
            footer: $"This link expires in {options.Value.EmailVerificationTokenLifetime.TotalHours:0} hours.",
            ct);

    public Task SendPasswordResetAsync(User user, string rawToken, CancellationToken ct) =>
        SendAsync(
            user,
            subject: "Reset your StockPilot password",
            intro: "We received a request to reset your password. If this wasn't you, you can ignore this email.",
            actionText: "Reset password",
            url: $"{BaseUrl}/reset-password#token={Uri.EscapeDataString(rawToken)}",
            footer: $"This link expires in {options.Value.PasswordResetTokenLifetime.TotalMinutes:0} minutes.",
            ct);

    public Task SendInvitationAsync(User user, string inviterName, string companyName, string rawToken, CancellationToken ct) =>
        SendAsync(
            user,
            subject: $"You're invited to {companyName} on StockPilot",
            intro: $"{inviterName} invited you to join {companyName} on StockPilot. Choose a password to get started.",
            actionText: "Accept invitation",
            url: $"{BaseUrl}/accept-invitation#token={Uri.EscapeDataString(rawToken)}",
            footer: $"This invitation expires in {options.Value.InvitationTokenLifetime.TotalDays:0} days.",
            ct);

    private string BaseUrl => options.Value.FrontendBaseUrl.TrimEnd('/');

    private async Task SendAsync(
        User user, string subject, string intro, string actionText, string url, string footer, CancellationToken ct)
    {
        var name = WebUtility.HtmlEncode(user.FirstName);
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;color:#0f172a">
              <h2 style="color:#4f46e5">StockPilot</h2>
              <p>Hi {name},</p>
              <p>{WebUtility.HtmlEncode(intro)}</p>
              <p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#4f46e5;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none">{actionText}</a></p>
              <p style="color:#64748b;font-size:13px">{footer}</p>
            </div>
            """;
        var text = $"Hi {user.FirstName},\n\n{intro}\n\n{actionText}: {url}\n\n{footer}";

        try
        {
            await sender.SendAsync(new EmailMessage(user.Email, subject, html, text), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send '{Subject}' email to user {UserId}", subject, user.Id);
        }
    }
}
