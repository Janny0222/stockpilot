using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using StockPilot.API.Middleware;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>Middleware-level checks: no database needed.</summary>
public sealed class TrustedProxyMiddlewareTests
{
    private const string Secret = "test-proxy-secret-0123456789-abcdefghijkl";

    private static async Task<IPAddress?> RunAsync(string? secretHeader, string? clientIp, string? configuredSecret = Secret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:ProxySecret"] = configuredSecret })
            .Build();

        IPAddress? seen = null;
        var middleware = new TrustedProxyClientIpMiddleware(ctx =>
        {
            seen = ctx.Connection.RemoteIpAddress;
            ctx.Request.Headers.ContainsKey(TrustedProxyClientIpMiddleware.SecretHeader).ShouldBeFalse();
            return Task.CompletedTask;
        }, config);

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1"); // the proxy hop
        if (secretHeader is not null) context.Request.Headers[TrustedProxyClientIpMiddleware.SecretHeader] = secretHeader;
        if (clientIp is not null) context.Request.Headers[TrustedProxyClientIpMiddleware.ClientIpHeader] = clientIp;

        await middleware.InvokeAsync(context);
        return seen;
    }

    [Fact]
    public async Task Uses_the_client_ip_when_the_proxy_secret_matches() =>
        (await RunAsync(Secret, "203.0.113.7")).ShouldBe(IPAddress.Parse("203.0.113.7"));

    [Fact]
    public async Task Ignores_a_spoofed_client_ip_without_the_secret() =>
        (await RunAsync(null, "203.0.113.7")).ShouldBe(IPAddress.Parse("10.0.0.1"));

    [Fact]
    public async Task Ignores_a_spoofed_client_ip_with_a_wrong_secret() =>
        (await RunAsync("guess", "203.0.113.7")).ShouldBe(IPAddress.Parse("10.0.0.1"));

    [Fact]
    public async Task Does_nothing_when_no_secret_is_configured() =>
        (await RunAsync(Secret, "203.0.113.7", configuredSecret: null)).ShouldBe(IPAddress.Parse("10.0.0.1"));

    [Fact]
    public async Task Ignores_a_malformed_client_ip() =>
        (await RunAsync(Secret, "not-an-ip")).ShouldBe(IPAddress.Parse("10.0.0.1"));
}

public sealed class ProxiedRateLimitApiFactory : ApiFactory
{
    public const string Secret = "test-proxy-secret-0123456789-abcdefghijkl";

    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["RateLimiting:AuthPermitLimit"] = "2",
        ["ForwardedHeaders:ProxySecret"] = Secret,
    };
}

/// <summary>End to end: behind the proxy, each visitor gets their OWN login rate-limit bucket.</summary>
public sealed class ProxiedRateLimitingTests(ProxiedRateLimitApiFactory factory) : IClassFixture<ProxiedRateLimitApiFactory>
{
    private static Task<HttpResponseMessage> LoginFrom(HttpClient client, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@example.test", password = "Whatever1A" }),
        };
        request.Headers.Add(TrustedProxyClientIpMiddleware.SecretHeader, ProxiedRateLimitApiFactory.Secret);
        request.Headers.Add(TrustedProxyClientIpMiddleware.ClientIpHeader, ip);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Visitors_behind_the_same_proxy_do_not_share_a_rate_limit()
    {
        var client = factory.CreateHttpsClient();

        (await LoginFrom(client, "198.51.100.1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginFrom(client, "198.51.100.1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginFrom(client, "198.51.100.1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // A different visitor arriving through the same proxy is unaffected.
        (await LoginFrom(client, "198.51.100.2")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
