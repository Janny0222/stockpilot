using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace StockPilot.API.Middleware;

/// <summary>
/// In production every browser request reaches the API through our Cloudflare Pages Function proxy,
/// so the TCP peer (and X-Forwarded-For) is a Cloudflare IP shared by many users. Rate limits and audit
/// logs need the real visitor IP.
///
/// The proxy sends the visitor IP in X-Client-IP together with a shared secret. We trust X-Client-IP ONLY
/// when the secret matches (constant-time compare). Someone calling the Render URL directly can't spoof
/// their IP, because they don't know the secret.
///
/// With ForwardedHeaders:RequireProxySecret=true (production), a request WITHOUT the secret is refused outright,
/// except health checks. The proxy then is the only way in, so nobody can go around it to the Render URL and pick
/// their own X-Forwarded-For to dodge the per-IP sign-in limits (found in a security review).
/// </summary>
internal sealed class TrustedProxyClientIpMiddleware
{
    public const string SecretHeader = "X-Proxy-Secret";
    public const string ClientIpHeader = "X-Client-IP";
    public const int MinSecretLength = 32;

    private readonly RequestDelegate _next;
    private readonly byte[]? _secret;
    private readonly bool _required;

    public TrustedProxyClientIpMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var configured = configuration["ForwardedHeaders:ProxySecret"];
        _required = configuration.GetValue<bool>("ForwardedHeaders:RequireProxySecret");

        // A short secret used to be silently ignored, which quietly made every visitor share Cloudflare's IPs (and one
        // sign-in rate limit). A misconfiguration must stop the app instead.
        if (!string.IsNullOrEmpty(configured) && configured.Length < MinSecretLength)
        {
            throw new InvalidOperationException($"ForwardedHeaders:ProxySecret must be at least {MinSecretLength} characters.");
        }

        if (_required && string.IsNullOrEmpty(configured))
        {
            throw new InvalidOperationException("ForwardedHeaders:RequireProxySecret is on but ForwardedHeaders:ProxySecret is not set.");
        }

        _secret = string.IsNullOrEmpty(configured) ? null : Encoding.UTF8.GetBytes(configured);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers;
        var trusted = _secret is not null &&
            headers.TryGetValue(SecretHeader, out var provided) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided.ToString()), _secret);

        // Never let the secret travel further (logs, downstream code).
        headers.Remove(SecretHeader);

        if (_required && !trusted && !context.Request.Path.StartsWithSegments("/health"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                status = 403,
                code = "DIRECT_ACCESS_BLOCKED",
                detail = "Use the StockPilot web app.",
            });
            return;
        }

        if (trusted && IPAddress.TryParse(headers[ClientIpHeader].ToString(), out var clientIp))
        {
            context.Connection.RemoteIpAddress = clientIp;
        }

        await _next(context);
    }
}
