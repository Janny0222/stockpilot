using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// Milestone M10's "done when": an Owner can invite, scope and deactivate a user end to end. Plus the guard rails
/// (no self-demotion or self-deactivation, no platform roles, no foreign warehouses), company settings and the
/// audit log.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettingsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<HttpResponseMessage> InviteAsync(HttpClient owner, string email, Guid roleId, params Guid[] warehouseIds) =>
        await owner.PostAsJsonAsync("/api/v1/users", new { email, firstName = "Ivy", lastName = "Invitee", roleId, warehouseIds });

    private static async Task<IReadOnlyList<Guid>> VisibleWarehousesAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/warehouses", Json);
        return page.GetProperty("items").EnumerateArray().Select(w => w.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task Owner_invites_scopes_and_deactivates_a_user_end_to_end()
    {
        var owner = await factory.RegisterCompanyAsync();
        var main = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var cebu = await factory.AddWarehouseAsync(owner.TenantId, "CEB");

        // The roles list is what the invite form offers: tenant roles only, never Super Admin.
        var roles = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/roles", Json);
        var roleIds = roles.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        roleIds.ShouldContain(SystemRoles.InventoryStaffId);
        roleIds.ShouldNotContain(SystemRoles.SuperAdminId);

        // 1. Invite an Inventory Staff member scoped to Cebu only.
        var email = TestHelpers.UniqueEmail("invitee");
        var invited = await ReadJsonAsync(await InviteAsync(owner.Client, email, SystemRoles.InventoryStaffId, cebu));
        var userId = invited.GetProperty("id").GetGuid();
        invited.GetProperty("status").GetString().ShouldBe("Invited");
        invited.GetProperty("warehouses").EnumerateArray().Single().GetProperty("code").GetString().ShouldBe("CEB");

        // No password yet, so nobody can sign in as them.
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestHelpers.Password })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // 2. The invitee opens the link: sees who invited them, then picks a password and is signed straight in.
        var token = factory.Emails.LatestToken(email, "/accept-invitation");
        var invitation = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/invitation", new { token }));
        invitation.GetProperty("roleName").GetString().ShouldBe("Inventory Staff");
        invitation.GetProperty("email").GetString().ShouldBe(email);

        var accepted = await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/accept-invitation", new { token, password = TestHelpers.Password }));
        client.Authorize(accepted.GetProperty("accessToken").GetString()!);

        // The link is single-use.
        var again = await client.PostAsJsonAsync("/api/v1/auth/accept-invitation", new { token, password = TestHelpers.Password });
        (await again.ReadProblemAsync()).Code.ShouldBe("TOKEN_INVALID");

        // 3. Scope: they only see Cebu. Re-scoping to Main applies on the very next request.
        (await VisibleWarehousesAsync(client)).ShouldBe([cebu]);
        await ReadJsonAsync(await owner.Client.PutAsJsonAsync($"/api/v1/users/{userId}",
            new { firstName = "Ivy", lastName = "Invitee", roleId = SystemRoles.InventoryStaffId, warehouseIds = new[] { main } }));
        (await VisibleWarehousesAsync(client)).ShouldBe([main]);

        var listed = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/users?status=Active", Json);
        listed.GetProperty("items").EnumerateArray().ShouldContain(u => u.GetProperty("id").GetGuid() == userId);

        // 4. Deactivate: their current token stops working at once and they can't sign back in.
        (await owner.Client.PostAsync($"/api/v1/users/{userId}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var login = await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestHelpers.Password });
        (await login.ReadProblemAsync()).Code.ShouldBe("ACCOUNT_DISABLED");

        // Reactivated, they can sign in again (the Owner never learned their password).
        (await owner.Client.PostAsync($"/api/v1/users/{userId}/activate", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.LoginAsync(email, TestHelpers.Password);
    }

    [Fact]
    public async Task Resending_an_invitation_retires_the_old_link()
    {
        var owner = await factory.RegisterCompanyAsync();
        var email = TestHelpers.UniqueEmail("resend");
        var userId = (await ReadJsonAsync(await InviteAsync(owner.Client, email, SystemRoles.SalesStaffId))).GetProperty("id").GetGuid();
        var first = factory.Emails.LatestToken(email, "/accept-invitation");

        (await owner.Client.PostAsync($"/api/v1/users/{userId}/resend-invitation", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var second = factory.Emails.LatestToken(email, "/accept-invitation");
        second.ShouldNotBe(first);

        var client = factory.CreateHttpsClient();
        (await (await client.PostAsJsonAsync("/api/v1/auth/invitation", new { token = first })).ReadProblemAsync()).Code.ShouldBe("TOKEN_INVALID");
        await ReadJsonAsync(await client.PostAsJsonAsync("/api/v1/auth/accept-invitation", new { token = second, password = TestHelpers.Password }));

        var resend = await owner.Client.PostAsync($"/api/v1/users/{userId}/resend-invitation", null);
        (await resend.ReadProblemAsync()).Code.ShouldBe("INVITATION_ACCEPTED");
    }

    [Fact]
    public async Task User_management_guard_rails()
    {
        var owner = await factory.RegisterCompanyAsync();
        var other = await factory.RegisterCompanyAsync();
        var otherWarehouse = await factory.DefaultWarehouseIdAsync(other.TenantId);

        // Owners can't lock themselves out.
        (await (await owner.Client.PostAsync($"/api/v1/users/{owner.UserId}/deactivate", null)).ReadProblemAsync()).Code.ShouldBe("CANNOT_DEACTIVATE_SELF");
        var demote = await owner.Client.PutAsJsonAsync($"/api/v1/users/{owner.UserId}",
            new { firstName = "Olivia", lastName = "Owner", roleId = SystemRoles.ManagerId, warehouseIds = Array.Empty<Guid>() });
        (await demote.ReadProblemAsync()).Code.ShouldBe("CANNOT_CHANGE_OWN_ROLE");

        // Renaming yourself is fine.
        var renamed = await ReadJsonAsync(await owner.Client.PutAsJsonAsync($"/api/v1/users/{owner.UserId}",
            new { firstName = "Liv", lastName = "Owner", roleId = SystemRoles.OwnerId }));
        renamed.GetProperty("isYou").GetBoolean().ShouldBeTrue();

        // Platform roles can't be handed out; warehouse-scoped roles need a warehouse, and it must be one of yours.
        var superAdmin = await InviteAsync(owner.Client, TestHelpers.UniqueEmail(), SystemRoles.SuperAdminId);
        superAdmin.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await superAdmin.ReadProblemAsync()).Errors!.ShouldContainKey("roleId");
        (await (await InviteAsync(owner.Client, TestHelpers.UniqueEmail(), SystemRoles.InventoryStaffId)).ReadProblemAsync()).Errors!.ShouldContainKey("warehouseIds");
        (await (await InviteAsync(owner.Client, TestHelpers.UniqueEmail(), SystemRoles.InventoryStaffId, otherWarehouse)).ReadProblemAsync()).Errors!.ShouldContainKey("warehouseIds");

        // One account per email across every company.
        (await InviteAsync(owner.Client, other.Email, SystemRoles.SalesStaffId)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Another company's user is invisible.
        (await owner.Client.GetAsync($"/api/v1/users/{other.UserId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.Client.PostAsync($"/api/v1/users/{other.UserId}/deactivate", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Only Owners manage users, company settings and the audit log.
        var manager = await factory.CreateUserAsync(owner.TenantId, SystemRoles.ManagerId);
        (await manager.Client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.Client.GetAsync("/api/v1/settings/company")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.Client.GetAsync("/api/v1/audit-logs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Company_settings_update_and_lock_the_currency_once_stock_moves()
    {
        var owner = await factory.RegisterCompanyAsync();
        var c = owner.Client;

        var settings = await c.GetFromJsonAsync<JsonElement>("/api/v1/settings/company", Json);
        settings.GetProperty("currency").GetString().ShouldBe("PHP");
        settings.GetProperty("currencyLocked").GetBoolean().ShouldBeFalse();

        var updated = await ReadJsonAsync(await c.PutAsJsonAsync("/api/v1/settings/company",
            new { name = "Renamed Trading", currency = "usd", timeZone = "Asia/Singapore", allowNegativeStock = true }));
        updated.GetProperty("currency").GetString().ShouldBe("USD");
        updated.GetProperty("timeZone").GetString().ShouldBe("Asia/Singapore");
        updated.GetProperty("allowNegativeStock").GetBoolean().ShouldBeTrue();
        (await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Json)).GetProperty("tenant").GetProperty("name").GetString().ShouldBe("Renamed Trading");

        (await (await c.PutAsJsonAsync("/api/v1/settings/company", new { name = "X", currency = "PESO", timeZone = "not a zone!", allowNegativeStock = false }))
            .ReadProblemAsync()).Errors!.Keys.ShouldBe(["name", "currency", "timeZone"], ignoreOrder: true);

        // After the first stock movement the currency is locked; everything else stays editable.
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var product = await ReadJsonAsync(await c.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Lamp", unitOfMeasure = "pcs", variants = new[] { new { sku = "L-1", costPrice = 10m, sellingPrice = 15m, reorderLevel = 0m } },
        }));
        var variant = product.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var stockIn = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/stock-in")
        {
            Content = JsonContent.Create(new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = 5m, unitCost = 10m } } }),
        };
        stockIn.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await c.SendAsync(stockIn)).EnsureSuccessStatusCode();

        (await c.GetFromJsonAsync<JsonElement>("/api/v1/settings/company", Json)).GetProperty("currencyLocked").GetBoolean().ShouldBeTrue();
        var locked = await c.PutAsJsonAsync("/api/v1/settings/company", new { name = "Renamed Trading", currency = "EUR", timeZone = "Asia/Singapore", allowNegativeStock = true });
        (await locked.ReadProblemAsync()).Code.ShouldBe("CURRENCY_LOCKED");
        await ReadJsonAsync(await c.PutAsJsonAsync("/api/v1/settings/company", new { name = "Final Name", currency = "USD", timeZone = "Asia/Manila", allowNegativeStock = false }));
    }

    [Fact]
    public async Task Audit_log_shows_who_changed_what_and_only_for_your_company()
    {
        var owner = await factory.RegisterCompanyAsync();
        var other = await factory.RegisterCompanyAsync();
        await ReadJsonAsync(await owner.Client.PutAsJsonAsync("/api/v1/settings/company",
            new { name = "Audited Co", currency = "PHP", timeZone = "Asia/Manila", allowNegativeStock = false }));

        var page = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?entityType=Tenant&action=Updated", Json);
        var entry = page.GetProperty("items").EnumerateArray().First();
        entry.GetProperty("userId").GetGuid().ShouldBe(owner.UserId);
        entry.GetProperty("userName").GetString().ShouldBe("Olivia Owner");
        var name = entry.GetProperty("changes").EnumerateArray().Single(ch => ch.GetProperty("field").GetString() == "Name");
        name.GetProperty("new").GetString().ShouldBe("Audited Co");

        // Sign-in events are there too, and secrets never are.
        var auth = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?action=Auth", Json);
        auth.GetProperty("items").EnumerateArray().ShouldContain(a => a.GetProperty("action").GetString() == "Auth.LoginSucceeded");
        var all = await owner.Client.GetStringAsync("/api/v1/audit-logs?pageSize=100");
        all.ShouldNotContain("PasswordHash");
        all.ShouldNotContain("SecurityStamp");

        var filters = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs/filters", Json);
        filters.GetProperty("entityTypes").EnumerateArray().Select(t => t.GetString()).ShouldContain("Tenant");

        // The other company sees none of it.
        var theirs = await other.Client.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?entityType=Tenant&action=Updated", Json);
        theirs.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }
}
