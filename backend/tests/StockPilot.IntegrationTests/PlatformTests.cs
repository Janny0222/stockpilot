using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// The Super Admin manages companies as accounts: list, look up, suspend, reactivate. Never their data, and no company
/// role can reach these endpoints.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Task<TestSession> AdminAsync() => factory.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Fact]
    public async Task Admin_sees_companies_as_accounts_with_counts_only()
    {
        var owner = await factory.RegisterCompanyAsync($"Platform Co {Guid.NewGuid():N}"[..24]);
        await factory.CreateUserAsync(owner.TenantId, SystemRoles.SalesStaffId);
        await owner.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Lamp", unitOfMeasure = "pcs", variants = new[] { new { sku = "L-1", costPrice = 10m, sellingPrice = 15m, reorderLevel = 0m } },
        });
        var admin = await AdminAsync();

        // Support usually gets asked "my account is xyz@…": search finds the company by its owner's email.
        var page = await admin.Client.GetFromJsonAsync<JsonElement>($"/api/v1/platform/tenants?search={Uri.EscapeDataString(owner.Email)}", Json);
        var company = page.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        company.GetProperty("id").GetGuid().ShouldBe(owner.TenantId);
        company.GetProperty("ownerEmail").GetString().ShouldBe(owner.Email);
        company.GetProperty("ownerName").GetString().ShouldBe("Olivia Owner");
        company.GetProperty("activeUsers").GetInt32().ShouldBe(2);
        company.GetProperty("warehouses").GetInt32().ShouldBe(1);
        company.GetProperty("products").GetInt32().ShouldBe(1);
        company.GetProperty("status").GetString().ShouldBe("Active");
        company.TryGetProperty("lastActivityAt", out var last).ShouldBeTrue();
        last.ValueKind.ShouldNotBe(JsonValueKind.Null);

        // Business data stays out of reach.
        (await admin.Client.GetAsync("/api/v1/products")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var metrics = await admin.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform/metrics", Json);
        metrics.GetProperty("companies").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        metrics.GetProperty("newCompaniesLast30Days").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        metrics.GetProperty("usersActiveLast7Days").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        (await admin.Client.GetAsync($"/api/v1/platform/tenants/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Suspending_locks_the_whole_company_out_and_reactivating_restores_it()
    {
        var owner = await factory.RegisterCompanyAsync();
        var staff = await factory.CreateUserAsync(owner.TenantId, SystemRoles.SalesStaffId);
        var admin = await AdminAsync();

        // A reason is required: it's what the company sees in its audit log later.
        (await admin.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{owner.TenantId}/suspend", new { reason = "" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var suspended = await OkJsonAsync(await admin.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{owner.TenantId}/suspend",
            new { reason = "Payment overdue since October" }));
        suspended.GetProperty("status").GetString().ShouldBe("Suspended");

        (await owner.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await staff.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var login = await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/auth/login", new { email = owner.Email, password = owner.Password });
        (await login.ReadProblemAsync()).Code.ShouldBe("TENANT_SUSPENDED");

        (await (await admin.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{owner.TenantId}/suspend", new { reason = "again" })).ReadProblemAsync())
            .Code.ShouldBe("ALREADY_SUSPENDED");
        var list = await admin.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform/tenants?status=Suspended&pageSize=100", Json);
        list.GetProperty("items").EnumerateArray().ShouldContain(t => t.GetProperty("id").GetGuid() == owner.TenantId);

        // Reactivated: everyone signs back in, nothing lost, and the company's audit log says why it happened.
        await OkJsonAsync(await admin.Client.PostAsync($"/api/v1/platform/tenants/{owner.TenantId}/activate", null));
        var back = await factory.LoginAsync(owner.Email, owner.Password);
        var audit = await back.Client.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?entityType=Tenant&pageSize=50", Json);
        var events = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        events.ShouldContain("Platform.TenantSuspended");
        events.ShouldContain("Platform.TenantActivated");
        audit.GetProperty("items").EnumerateArray()
            .Single(a => a.GetProperty("action").GetString() == "Platform.TenantSuspended")
            .GetProperty("changes")[0].GetProperty("new").GetString().ShouldBe("Payment overdue since October");
    }

    [Fact]
    public async Task No_company_role_can_reach_platform_endpoints()
    {
        var owner = await factory.RegisterCompanyAsync();
        (await owner.Client.GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Client.GetAsync("/api/v1/platform/metrics")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{owner.TenantId}/suspend", new { reason = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateHttpsClient().GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
