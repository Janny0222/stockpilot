using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StockPilot.Application.Common.Abstractions;

namespace StockPilot.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Smtp" (Mailpit locally, Resend/any SMTP in prod) or "Log" (dev without Docker: prints the email).</summary>
    public string Provider { get; set; } = "Smtp";
    public string FromAddress { get; set; } = "no-reply@stockpilot.local";
    public string FromName { get; set; } = "StockPilot";
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 1025;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }

    /// <summary>"None" for Mailpit, "StartTls" (587) or "SslOnConnect" (465) for real providers.</summary>
    public string SmtpSecurity { get; set; } = "None";
}

/// <summary>
/// One implementation for every environment: Mailpit locally, any SMTP provider in production
/// (Resend exposes SMTP), so no vendor SDK is needed.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        var security = Enum.TryParse<SecureSocketOptions>(o.SmtpSecurity, ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.None;

        using var client = new SmtpClient();
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, security, cancellationToken);
        if (!string.IsNullOrEmpty(o.SmtpUsername))
        {
            await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>
/// Fallback when no email provider is set: writes the email to the log instead of sending it. In Development the whole
/// body is logged (that's how you click verification and invite links locally). Anywhere else only who and what: the
/// body carries sign-in links (password reset, invitations), and those must never sit in hosted logs.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            logger.LogWarning("[DEV EMAIL] To: {To} | Subject: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        }
        else
        {
            logger.LogWarning("Email not sent (Email:Provider=Log): To {To} | Subject {Subject}. Configure SMTP to deliver it.", message.To, message.Subject);
        }

        return Task.CompletedTask;
    }
}
