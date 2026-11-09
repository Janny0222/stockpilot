using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_verify_login_then_me_returns_owner_profile()
    {
        var session = await factory.RegisterCompanyAsync("Acme Trading");

        var me = await session.Client.GetFromJsonAsync<MeDto>("/api/v1/auth/me");

        me!.Email.ShouldBe(session.Email);
        me.Role.Name.ShouldBe("Owner");
        me.Tenant!.Name.ShouldBe("Acme Trading");
        me.Tenant.Currency.ShouldBe("PHP");
        me.Permissions.ShouldContain(Permissions.Users.Manage);
        me.Permissions.ShouldNotContain(Permissions.Platform.TenantsView);
    }

    [Fact]
    public async Task Registration_creates_tenant_owner_and_default_warehouse_atomically()
    {
        var session = await factory.RegisterCompanyAsync();

        var (tenants, users, warehouses) = await factory.WithDbAsync(async db => (
            await db.Tenants.CountAsync(t => t.Id == session.TenantId),
            await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == session.TenantId),
            await db.Warehouses.IgnoreQueryFilters().CountAsync(w => w.TenantId == session.TenantId && w.IsDefault)));

        (tenants, users, warehouses).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Login_before_email_verification_is_forbidden()
    {
        var client = factory.CreateHttpsClient();
        var email = TestHelpers.UniqueEmail();
        (await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Unverified Co", firstName = "U", lastName = "V", email, password = TestHelpers.Password,
        })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestHelpers.Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code.ShouldBe("EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task Duplicate_email_registration_returns_409()
    {
        var session = await factory.RegisterCompanyAsync();

        var response = await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Copycat", firstName = "C", lastName = "C", email = session.Email.ToUpperInvariant(), password = TestHelpers.Password,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.ShouldBe("EMAIL_TAKEN");
    }

    [Fact]
    public async Task Invalid_registration_returns_400_with_field_errors()
    {
        var response = await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "", firstName = "", lastName = "x", email = "nope", password = "weak",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadProblemAsync();
        problem.Code.ShouldBe("VALIDATION_FAILED");
        problem.Errors!.Keys.ShouldBe(["companyName", "firstName", "email", "password"], ignoreOrder: true);
    }

    [Fact]
    public async Task Wrong_password_is_401_and_account_locks_after_max_attempts()
    {
        var session = await factory.RegisterCompanyAsync();
        var client = factory.CreateHttpsClient();

        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "Wrong-Passw0rd" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await wrong.ReadProblemAsync()).Code.ShouldBe("INVALID_CREDENTIALS");
        }

        // Correct password is now refused until the lockout expires.
        var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = TestHelpers.Password });
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await locked.ReadProblemAsync()).Code.ShouldBe("ACCOUNT_LOCKED");
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_are_indistinguishable()
    {
        var session = await factory.RegisterCompanyAsync();
        var client = factory.CreateHttpsClient();

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = TestHelpers.UniqueEmail(), password = "Whatever1A" });
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = "Whatever1A" });

        unknown.StatusCode.ShouldBe(wrong.StatusCode);
        (await unknown.ReadProblemAsync()).Detail.ShouldBe((await wrong.ReadProblemAsync()).Detail);
    }

    [Fact]
    public async Task Refresh_cookie_has_secure_attributes()
    {
        var session = await factory.RegisterCompanyAsync();
        var response = await factory.CreateHttpsClient(handleCookies: false)
            .PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = TestHelpers.Password });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("sp_rt=", StringComparison.Ordinal)).ToLowerInvariant();

        cookie.ShouldContain("httponly");
        cookie.ShouldContain("secure");
        cookie.ShouldContain("samesite=strict");
        cookie.ShouldContain("path=/api/v1/auth");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reusing_an_old_one_revokes_the_whole_family()
    {
        var session = await factory.RegisterCompanyAsync();
        var client = factory.CreateHttpsClient(handleCookies: false);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = TestHelpers.Password });
        var tokenA = login.RefreshCookie()!;

        var first = await RefreshWith(client, tokenA);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokenB = first.RefreshCookie()!;
        tokenB.ShouldNotBe(tokenA);

        // Attacker (or bug) replays the already-rotated token A → rejected...
        (await RefreshWith(client, tokenA)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and the legitimate holder's newer token B is now dead too (family revoked).
        (await RefreshWith(client, tokenB)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var reasons = await factory.WithDbAsync(db => db.RefreshTokens
            .Where(t => t.UserId == session.UserId).Select(t => t.RevokeReason).ToListAsync());
        reasons.ShouldContain(RefreshTokenRevokeReason.ReuseDetected);
    }

    [Fact]
    public async Task Refresh_returns_a_working_access_token()
    {
        var session = await factory.RegisterCompanyAsync();

        // The session's client has a cookie container holding the refresh cookie from login.
        var refreshed = await session.Client.PostAsync("/api/v1/auth/refresh", null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await refreshed.Content.ReadFromJsonAsync<TokenResponse>();

        var client = factory.CreateHttpsClient();
        client.Authorize(tokens!.AccessToken);
        (await client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_without_cookie_is_401()
    {
        var response = await factory.CreateHttpsClient(handleCookies: false).PostAsync("/api/v1/auth/refresh", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Code.ShouldBe("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var session = await factory.RegisterCompanyAsync();
        var client = factory.CreateHttpsClient(handleCookies: false);
        var token = (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = TestHelpers.Password }))
            .RefreshCookie()!;

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"sp_rt={token}");
        (await client.SendAsync(logout)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RefreshWith(client, token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_everywhere_invalidates_existing_access_tokens_immediately()
    {
        var session = await factory.RegisterCompanyAsync();
        var otherDevice = await factory.LoginAsync(session.Email, session.Password);

        (await session.Client.PostAsync("/api/v1/auth/logout-all", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The access token is still within its 15-minute lifetime, but the security stamp changed.
        (await otherDevice.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await otherDevice.Client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_reset_sets_new_password_and_kills_all_existing_sessions()
    {
        var session = await factory.RegisterCompanyAsync();
        var anonymous = factory.CreateHttpsClient();

        (await anonymous.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = session.Email })).StatusCode
            .ShouldBe(HttpStatusCode.Accepted);
        var token = factory.Emails.LatestToken(session.Email, "/reset-password");

        const string newPassword = "Brand-N3w-Secret";
        (await anonymous.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword })).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await session.Client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var oldLogin = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = session.Email, password = session.Password });
        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await factory.LoginAsync(session.Email, newPassword);

        // Reset links are single-use.
        var reuse = await anonymous.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "An0ther-Secret" });
        reuse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await reuse.ReadProblemAsync()).Code.ShouldBe("TOKEN_INVALID");
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_looks_exactly_like_success()
    {
        var before = factory.Emails.Sent.Count;

        var response = await factory.CreateHttpsClient()
            .PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = TestHelpers.UniqueEmail() });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Emails.Sent.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Sessions_list_marks_the_current_one_and_revoking_another_users_session_is_404()
    {
        var alice = await factory.RegisterCompanyAsync();
        var bob = await factory.RegisterCompanyAsync();

        var aliceSessions = (await alice.Client.GetFromJsonAsync<List<SessionItem>>("/api/v1/auth/sessions"))!;
        aliceSessions.ShouldContain(s => s.IsCurrent);

        // Bob tries to revoke Alice's session by ID (IDOR attempt).
        var attempt = await bob.Client.DeleteAsync($"/api/v1/auth/sessions/{aliceSessions[0].Id}");
        attempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Alice can revoke her own.
        (await alice.Client.DeleteAsync($"/api/v1/auth/sessions/{aliceSessions[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static Task<HttpResponseMessage> RefreshWith(HttpClient client, string refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"sp_rt={refreshToken}");
        return client.SendAsync(request);
    }

    private sealed record MeDto(string Email, RoleDto Role, List<string> Permissions, TenantDto? Tenant);

    private sealed record RoleDto(Guid Id, string Name);

    private sealed record TenantDto(Guid Id, string Name, string Currency);

    private sealed record SessionItem(Guid Id, bool IsCurrent);
}
