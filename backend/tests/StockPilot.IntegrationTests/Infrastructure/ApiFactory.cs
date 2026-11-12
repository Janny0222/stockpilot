using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace StockPilot.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (same Program.cs, middleware, auth, EF model) against a REAL PostgreSQL.
/// Never EF InMemory: it has no transactions, row locks, unique indexes or query-filter SQL, so
/// tests would pass against behaviour production doesn't have.
///
/// Database source:
///   STOCKPILOT_TEST_DB set  → creates a throwaway database on that server (e.g. local Postgres on 5433)
///   otherwise               → starts a disposable postgres:17 container via Testcontainers (needs Docker)
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _serverConnectionString;
    private string? _databaseName;

    public string ConnectionString { get; private set; } = string.Empty;
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Lets subclasses tighten settings (e.g. a low rate limit).</summary>
    protected virtual IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("STOCKPILOT_TEST_DB");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _serverConnectionString = external;
            _databaseName = $"stockpilot_test_{Guid.NewGuid():N}";
            await using var conn = new NpgsqlConnection(external);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
            ConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        _ = Services; // build the host now: runs migrations once, before any test
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (not ConfigureAppConfiguration) so values are visible to Program.cs before Build().
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789-abcdefghijklmnop");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Auth:SessionValidationCacheDuration", "00:00:00"); // see revocations immediately
        builder.UseSetting("Jobs:Enabled", "false"); // tests run jobs explicitly, never on a timer
        builder.UseSetting("Auth:RefreshTokenReuseGracePeriod", "00:00:00");   // any reuse = theft, deterministically
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
        builder.UseSetting("RateLimiting:GlobalPermitLimit", "100000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Platform:SuperAdmin:Email", SuperAdminEmail);
        builder.UseSetting("Platform:SuperAdmin:Password", SuperAdminPassword);
        foreach (var (key, value) in ExtraSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            // Test-only endpoints (e.g. one guarded by a permission Sales Staff lacks).
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }

    public const string SuperAdminEmail = "root@platform.test";
    public const string SuperAdminPassword = "R00t-Platform!";

    /// <summary>HTTPS base address so the Secure refresh cookie is stored and sent by the cookie container.</summary>
    public HttpClient CreateHttpsClient(bool handleCookies = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = handleCookies,
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// A DI scope whose ICurrentUser is the given tenant/user, for tests that exercise the
    /// DbContext guards directly (query filters, write interceptor).
    /// </summary>
    public AsyncServiceScope CreateScopeAs(Guid? tenantId, Guid? userId = null)
    {
        var scope = Services.CreateAsyncScope();
        var claims = new List<Claim> { new(StockPilotClaims.UserId, (userId ?? Guid.NewGuid()).ToString()) };
        if (tenantId is { } t)
        {
            claims.Add(new Claim(StockPilotClaims.TenantId, t.ToString()));
        }

        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
            RequestServices = scope.ServiceProvider,
        };
        return scope;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (_serverConnectionString is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(_serverConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>Captures outgoing emails so tests can follow verification / reset links.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Extracts the raw token from the most recent email to <paramref name="to"/> whose link targets <paramref name="path"/>.</summary>
    public string LatestToken(string to, string path)
    {
        var email = _sent.Reverse().FirstOrDefault(m =>
            m.To.Equals(to, StringComparison.OrdinalIgnoreCase) && m.TextBody.Contains(path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No '{path}' email was sent to {to}.");

        return Uri.UnescapeDataString(TokenPattern().Match(email.TextBody).Groups[1].Value);
    }

    [GeneratedRegex(@"#token=([^\s]+)")]
    private static partial Regex TokenPattern();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
