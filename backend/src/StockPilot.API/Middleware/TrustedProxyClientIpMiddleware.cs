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
/// </summary>
internal sealed class TrustedProxyClientIpMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string SecretHeader = "X-Proxy-Secret";
    public const string ClientIpHeader = "X-Client-IP";

    private readonly byte[]? _secret = configuration["ForwardedHeaders:ProxySecret"] is { Length: >= 32 } s
        ? Encoding.UTF8.GetBytes(s)
        : null;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers;

        if (_secret is not null &&
            headers.TryGetValue(SecretHeader, out var provided) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided.ToString()), _secret) &&
            IPAddress.TryParse(headers[ClientIpHeader].ToString(), out var clientIp))
        {
            context.Connection.RemoteIpAddress = clientIp;
        }

        // Never let the secret travel further (logs, downstream code).
        headers.Remove(SecretHeader);
        return next(context);
    }
}
