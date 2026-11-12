using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// One test per finding from the M12 security review, so each fix stays fixed. See docs/14-security-review.md.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityRegressionTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record World(TestSession Owner, Guid Main, Guid Cebu, Guid Cheap, Guid Pricey);

    /// <summary>Two items in Main: "Cheap" (cost 1, 100 units) and "Pricey" (cost 500, 1 unit). Cebu is a second warehouse.</summary>
    private async Task<World> SetUpAsync()
    {
        var owner = await factory.RegisterCompanyAsync();
        var main = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var cebu = await factory.AddWarehouseAsync(owner.TenantId, "CEB");

        async Task<Guid> ProductAsync(string name, string sku, decimal cost, decimal qty)
        {
            var p = await (await owner.Client.PostAsJsonAsync("/api/v1/products", new
            {
                name, unitOfMeasure = "pcs", variants = new[] { new { sku, costPrice = cost, sellingPrice = cost * 2, reorderLevel = 0m } },
            })).Content.ReadFromJsonAsync<JsonElement>(Json);
            var variant = p.GetProperty("variants")[0].GetProperty("id").GetGuid();
            await PostIdempotentAsync(owner.Client, "/api/v1/inventory/stock-in", new { warehouseId = main, lines = new[] { new { variantId = variant, quantity = qty, unitCost = cost } } });
            return variant;
        }

        return new World(owner, main, cebu, await ProductAsync("Alpha Cheap", "A-1", 1m, 100m), await ProductAsync("Zulu Pricey", "Z-1", 500m, 1m));
    }

    private static async Task<HttpResponseMessage> PostIdempotentAsync(HttpClient client, string url, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Inventory_staff_cannot_read_stock_of_a_warehouse_they_are_not_assigned_to()
    {
        var w = await SetUpAsync();
        var staff = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.InventoryStaffId, w.Cebu);

        (await staff.Client.GetAsync($"/api/v1/inventory/variants?search=a&warehouseId={w.Main}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staff.Client.GetAsync($"/api/v1/inventory/variants?search=a&warehouseId={w.Cebu}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sorting_by_value_does_not_reveal_the_cost_ranking_to_roles_without_cost_access()
    {
        var w = await SetUpAsync();
        var sales = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.SalesStaffId);

        async Task<List<string>> OrderAsync(TestSession who, string sort) =>
            (await who.Client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/stock?sort={sort}", Json))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sku").GetString()!).ToList();

        // The owner's value sort puts the 500-cost item first; for Sales Staff the sort is ignored (name order).
        (await OrderAsync(w.Owner, "value:desc")).ShouldBe(["Z-1", "A-1"]);
        (await OrderAsync(sales, "value:desc")).ShouldBe(["A-1", "Z-1"]);
    }

    [Fact]
    public async Task Inventory_staff_cannot_move_the_average_cost_with_a_typed_unit_cost()
    {
        var w = await SetUpAsync();
        var staff = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.InventoryStaffId, w.Main);

        // 100 units at cost 1 already; staff "receives" 100 more claiming a cost of 999.
        (await PostIdempotentAsync(staff.Client, "/api/v1/inventory/stock-in",
            new { warehouseId = w.Main, lines = new[] { new { variantId = w.Cheap, quantity = 100m, unitCost = 999m } } })).EnsureSuccessStatusCode();

        var stock = await w.Owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/stock?warehouseId={w.Main}&search=A-1", Json);
        stock.GetProperty("items")[0].GetProperty("averageUnitCost").GetDecimal().ShouldBe(1m); // catalogue cost used, not 999
    }

    [Fact]
    public async Task An_idempotency_key_cannot_replay_another_users_response()
    {
        var w = await SetUpAsync();
        var manager = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.ManagerId);
        var key = Guid.NewGuid().ToString("N");
        var body = new { warehouseId = w.Main, lines = new[] { new { variantId = w.Cheap, quantity = 1m } } };

        (await PostIdempotentAsync(w.Owner.Client, "/api/v1/inventory/stock-out", body, key)).EnsureSuccessStatusCode();

        // Same company, same key, same body, different person: refused, not served the owner's stored result.
        var replay = await PostIdempotentAsync(manager.Client, "/api/v1/inventory/stock-out", body, key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await replay.ReadProblemAsync()).Code.ShouldBe("IDEMPOTENCY_KEY_REUSED");
    }
}

public sealed class InviteLimitedApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?> { ["RateLimiting:InvitesPerHour"] = "2" };
}

/// <summary>Invitations send email from our domain, so a company gets a capped number per hour.</summary>
public sealed class InviteRateLimitTests(InviteLimitedApiFactory factory) : IClassFixture<InviteLimitedApiFactory>
{
    [Fact]
    public async Task Invitations_are_capped_per_company()
    {
        var owner = await factory.RegisterCompanyAsync();
        async Task<HttpStatusCode> InviteAsync(TestSession who) => (await who.Client.PostAsJsonAsync("/api/v1/users", new
        {
            email = TestHelpers.UniqueEmail("invitee"), firstName = "A", lastName = "B", roleId = SystemRoles.SalesStaffId, warehouseIds = Array.Empty<Guid>(),
        })).StatusCode;

        (await InviteAsync(owner)).ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(owner)).ShouldBe(HttpStatusCode.Created);
        (await InviteAsync(owner)).ShouldBe(HttpStatusCode.TooManyRequests);

        // Another company has its own allowance.
        (await InviteAsync(await factory.RegisterCompanyAsync())).ShouldBe(HttpStatusCode.Created);
    }
}
