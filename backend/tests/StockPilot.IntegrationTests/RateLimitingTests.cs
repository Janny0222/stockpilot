using System.Net;
using System.Net.Http.Json;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>Separate host (and database) with a deliberately tiny auth limit.</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings =>
        new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "3" };
}

public sealed class RateLimitingTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Auth_endpoints_return_429_with_retry_after_once_the_limit_is_hit()
    {
        var client = factory.CreateHttpsClient();
        var body = new { email = "nobody@example.test", password = "Whatever1A" };

        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/login", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", body);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await limited.ReadProblemAsync()).Code.ShouldBe("RATE_LIMITED");
    }
}
