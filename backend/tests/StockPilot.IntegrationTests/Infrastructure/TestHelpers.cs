using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Identity;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.IntegrationTests.Infrastructure;

public sealed record RegisterResponse(Guid TenantId, Guid UserId, bool EmailVerificationRequired);

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record ProblemResponse(int Status, string Code, string? Detail, Dictionary<string, string[]>? Errors);

/// <summary>A signed-in test user with an HttpClient that sends their bearer token.</summary>
public sealed record TestSession(HttpClient Client, string AccessToken, Guid TenantId, Guid UserId, string Email, string Password);

public static class TestHelpers
{
    public const string Password = "Test-Passw0rd!";

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.test";

    public static async Task<ProblemResponse> ReadProblemAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    public static void Authorize(this HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Full happy path: register company → follow verification email → log in.</summary>
    public static async Task<TestSession> RegisterCompanyAsync(this ApiFactory factory, string? companyName = null)
    {
        var client = factory.CreateHttpsClient();
        var email = UniqueEmail("owner");

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = companyName ?? $"Company {Guid.NewGuid():N}"[..20],
            firstName = "Olivia",
            lastName = "Owner",
            email,
            password = Password,
            currency = "PHP",
            timeZone = "Asia/Manila",
        });
        register.StatusCode.ShouldBe(HttpStatusCode.Created, await register.Content.ReadAsStringAsync());
        var registered = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!;

        var token = factory.Emails.LatestToken(email, "/verify-email");
        (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var session = await factory.LoginAsync(email, Password, client);
        return session with { TenantId = registered.TenantId, UserId = registered.UserId };
    }

    public static async Task<TestSession> LoginAsync(this ApiFactory factory, string email, string password, HttpClient? client = null)
    {
        client ??= factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.Authorize(tokens.AccessToken);
        return new TestSession(client, tokens.AccessToken, Guid.Empty, Guid.Empty, email, password);
    }

    /// <summary>
    /// Creates a verified user with a given role directly in the DB (user management endpoints arrive
    /// in the Settings module), then signs them in through the real API.
    /// </summary>
    public static async Task<TestSession> CreateUserAsync(
        this ApiFactory factory, Guid tenantId, Guid roleId, params Guid[] warehouseIds)
    {
        var email = UniqueEmail("staff");
        Guid userId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = User.CreateTenantUser(tenantId, roleId, email, "Sam", "Staff");
            user.SetPasswordHash(hasher.Hash(Password));
            user.ConfirmEmail();
            db.Users.Add(user);
            db.UserWarehouseAccess.AddRange(warehouseIds.Select(w => new UserWarehouseAccess(tenantId, user.Id, w)));
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        var session = await factory.LoginAsync(email, Password);
        return session with { TenantId = tenantId, UserId = userId };
    }

    public static async Task<Guid> AddWarehouseAsync(this ApiFactory factory, Guid tenantId, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var warehouse = new StockPilot.Domain.Warehousing.Warehouse(tenantId, code, $"Warehouse {code}", isDefault: false);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse.Id;
    }

    public static async Task<T> WithDbAsync<T>(this ApiFactory factory, Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static Task<Guid> DefaultWarehouseIdAsync(this ApiFactory factory, Guid tenantId) =>
        factory.WithDbAsync(db => db.Warehouses.IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && w.IsDefault).Select(w => w.Id).SingleAsync());

    /// <summary>Pulls the raw refresh token out of a Set-Cookie header.</summary>
    public static string? RefreshCookie(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("sp_rt=", StringComparison.Ordinal))?["sp_rt=".Length..]
            : null;
}
