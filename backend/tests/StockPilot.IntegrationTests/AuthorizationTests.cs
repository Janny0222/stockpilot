using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests(ApiFactory factory)
{
    [Fact]
    public async Task Endpoints_require_authentication_by_default()
    {
        var client = factory.CreateHttpsClient();

        (await client.GetAsync("/api/v1/warehouses")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_and_api_docs_are_public()
    {
        var client = factory.CreateHttpsClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var session = await factory.RegisterCompanyAsync();
        var parts = session.AccessToken.Split('.');
        // Flip the payload to claim a different tenant; the signature no longer matches.
        var forged = $"{parts[0]}.{Base64UrlEncoder.Encode("{\"sub\":\"x\",\"tid\":\"" + Guid.NewGuid() + "\"}")}.{parts[2]}";

        var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        (await client.GetAsync("/api/v1/warehouses")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var session = await factory.RegisterCompanyAsync();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "stockpilot-api",
            Audience = "stockpilot-app",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = session.UserId.ToString(),
                ["tid"] = session.TenantId.ToString(),
                ["rid"] = SystemRoles.OwnerId.ToString(),
                ["sstamp"] = "anything",
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey("attacker-controlled-key-0123456789-abcdef"u8.ToArray()), SecurityAlgorithms.HmacSha256),
        });

        var client = factory.CreateHttpsClient();
        client.Authorize(token);

        (await client.GetAsync("/api/v1/warehouses")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Permission_is_enforced_per_role()
    {
        var owner = await factory.RegisterCompanyAsync();
        var sales = await factory.CreateUserAsync(owner.TenantId, SystemRoles.SalesStaffId);
        var manager = await factory.CreateUserAsync(owner.TenantId, SystemRoles.ManagerId);

        (await sales.Client.GetAsync("/api/test/adjust-stock")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.Client.GetAsync("/api/test/adjust-stock")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.GetAsync("/api/test/platform")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Super_admin_has_platform_permissions_but_no_tenant_data_access()
    {
        var admin = await factory.LoginAsync(ApiFactory.SuperAdminEmail, ApiFactory.SuperAdminPassword);

        (await admin.Client.GetAsync("/api/test/platform")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.Client.GetAsync("/api/v1/warehouses")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var me = await admin.Client.GetFromJsonAsync<MeDto>("/api/v1/auth/me");
        me!.IsPlatformAdmin.ShouldBeTrue();
        me.Tenant.ShouldBeNull();
    }

    [Fact]
    public async Task Deactivated_user_is_locked_out_immediately_even_with_a_valid_token()
    {
        var owner = await factory.RegisterCompanyAsync();
        var staff = await factory.CreateUserAsync(owner.TenantId, SystemRoles.SalesStaffId);
        (await staff.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == staff.UserId);
            user.Deactivate();
            return await db.SaveChangesAsync();
        });

        (await staff.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Suspended_company_cannot_sign_in_and_existing_tokens_stop_working()
    {
        var owner = await factory.RegisterCompanyAsync();

        await factory.WithDbAsync(async db =>
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == owner.TenantId);
            tenant.Suspend();
            return await db.SaveChangesAsync();
        });

        (await owner.Client.GetAsync("/api/v1/warehouses")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var login = await factory.CreateHttpsClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { email = owner.Email, password = owner.Password });
        login.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await login.ReadProblemAsync()).Code.ShouldBe("TENANT_SUSPENDED");
    }

    private sealed record MeDto(bool IsPlatformAdmin, object? Tenant);
}
