using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// Notifications are written in the same transaction as the change, to the right people (by permission and warehouse),
/// once per problem, and each person only ever sees and changes their own.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Company(TestSession Owner, Guid Main, Guid Variant);

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task PostIdempotentAsync(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    /// <summary>One product (reorder level 15) with 20 units in the main warehouse.</summary>
    private async Task<Company> SetUpAsync()
    {
        var owner = await factory.RegisterCompanyAsync();
        var main = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var product = await OkJsonAsync(await owner.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "LED Bulb", unitOfMeasure = "pcs", variants = new[] { new { sku = "LED-9W", costPrice = 50m, sellingPrice = 95m, reorderLevel = 15m } },
        }));
        var variant = product.GetProperty("variants")[0].GetProperty("id").GetGuid();
        await StockInAsync(owner.Client, main, variant, 20);
        return new Company(owner, main, variant);
    }

    private static Task StockInAsync(HttpClient c, Guid warehouse, Guid variant, decimal qty) =>
        PostIdempotentAsync(c, "/api/v1/inventory/stock-in", new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = qty, unitCost = 50m } } });

    private static Task StockOutAsync(HttpClient c, Guid warehouse, Guid variant, decimal qty) =>
        PostIdempotentAsync(c, "/api/v1/inventory/stock-out", new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = qty } } });

    private static async Task<List<JsonElement>> InboxAsync(TestSession who, string type = "")
    {
        var page = await who.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?pageSize=100", Json);
        return page.GetProperty("items").EnumerateArray().Where(n => type == "" || n.GetProperty("type").GetString() == type).ToList();
    }

    [Fact]
    public async Task Low_stock_alerts_fire_once_per_problem_and_only_for_people_who_reorder()
    {
        var c = await SetUpAsync();
        var manager = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.ManagerId);
        var staff = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.InventoryStaffId, c.Main);

        await StockOutAsync(c.Owner.Client, c.Main, c.Variant, 3);   // 17: still fine
        (await InboxAsync(c.Owner, "LowStock")).ShouldBeEmpty();

        await StockOutAsync(staff.Client, c.Main, c.Variant, 4);     // 13: crosses the reorder level of 15
        await StockOutAsync(c.Owner.Client, c.Main, c.Variant, 2);   // 11: still low, no repeat
        var low = (await InboxAsync(c.Owner, "LowStock")).ShouldHaveSingleItem();
        low.GetProperty("title").GetString().ShouldBe("LED Bulb is running low");
        // A snapshot of the moment it crossed (13 left), not the current balance.
        low.GetProperty("message").GetString()!.ShouldContain("LED-9W: 13 available");
        low.GetProperty("entityType").GetString().ShouldBe("LowStock");
        low.GetProperty("entityId").GetGuid().ShouldBe(c.Main);

        await StockOutAsync(c.Owner.Client, c.Main, c.Variant, 11);  // 0: out of stock is worse, so it alerts again
        (await InboxAsync(c.Owner, "LowStock")).Select(n => n.GetProperty("title").GetString())
            .ShouldBe(["LED Bulb is out of stock", "LED Bulb is running low"]);

        // Managers reorder too; Inventory Staff can't buy, so they aren't alerted (even though they took the stock out).
        (await InboxAsync(manager, "LowStock")).Count.ShouldBe(2);
        (await InboxAsync(staff, "LowStock")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reserving_stock_on_a_sales_order_can_tip_an_item_into_low_stock()
    {
        var c = await SetUpAsync();
        var customer = (await OkJsonAsync(await c.Owner.Client.PostAsJsonAsync("/api/v1/customers", new { code = "C1", name = "BuildRight", paymentTermsDays = 0 })))
            .GetProperty("id").GetGuid();
        var order = (await OkJsonAsync(await c.Owner.Client.PostAsJsonAsync("/api/v1/sales-orders", new
        {
            customerId = customer, warehouseId = c.Main, orderDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            lines = new[] { new { variantId = c.Variant, quantity = 8m, unitPrice = 95m } },
        }))).GetProperty("id").GetGuid();

        // A Sales Staff member confirms; the warehouse team (sales.fulfil for that warehouse) hears about it, they don't.
        var sales = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.SalesStaffId);
        var picker = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.InventoryStaffId, c.Main);
        var otherSite = await factory.AddWarehouseAsync(c.Owner.TenantId, "CEB");
        var elsewhere = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.InventoryStaffId, otherSite);

        (await sales.Client.PostAsync($"/api/v1/sales-orders/{order}/confirm", null)).EnsureSuccessStatusCode();

        // 20 - 8 reserved = 12 available ≤ 15.
        (await InboxAsync(c.Owner, "LowStock")).ShouldHaveSingleItem();

        var ready = (await InboxAsync(picker, "SalesOrderReadyToShip")).ShouldHaveSingleItem();
        ready.GetProperty("title").GetString()!.ShouldEndWith("is ready to ship");
        ready.GetProperty("message").GetString()!.ShouldContain("BuildRight");
        ready.GetProperty("entityId").GetGuid().ShouldBe(order);
        (await InboxAsync(c.Owner, "SalesOrderReadyToShip")).ShouldHaveSingleItem();
        (await InboxAsync(sales, "SalesOrderReadyToShip")).ShouldBeEmpty();     // the person who confirmed
        (await InboxAsync(elsewhere, "SalesOrderReadyToShip")).ShouldBeEmpty(); // a different warehouse
    }

    [Fact]
    public async Task Purchase_orders_notify_approvers_then_the_receiving_warehouse()
    {
        var c = await SetUpAsync();
        var manager = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.ManagerId);
        var receiver = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.InventoryStaffId, c.Main);
        var supplier = (await OkJsonAsync(await c.Owner.Client.PostAsJsonAsync("/api/v1/suppliers", new { code = "S1", name = "Philips Lighting", paymentTermsDays = 30 })))
            .GetProperty("id").GetGuid();
        var po = (await OkJsonAsync(await manager.Client.PostAsJsonAsync("/api/v1/purchase-orders", new
        {
            supplierId = supplier, warehouseId = c.Main, orderDate = "2026-11-09", expectedDate = "2026-11-16",
            lines = new[] { new { variantId = c.Variant, quantity = 10m, unitCost = 50m } },
        }))).GetProperty("id").GetGuid();

        (await manager.Client.PostAsync($"/api/v1/purchase-orders/{po}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var approve = (await InboxAsync(c.Owner, "PurchaseOrderAwaitingApproval")).ShouldHaveSingleItem();
        approve.GetProperty("message").GetString()!.ShouldContain("Philips Lighting, 1 line");
        approve.GetProperty("message").GetString()!.ShouldNotContain("500"); // amounts are cost data, kept out of the text
        (await InboxAsync(manager, "PurchaseOrderAwaitingApproval")).ShouldBeEmpty(); // they submitted it
        (await InboxAsync(receiver, "PurchaseOrderAwaitingApproval")).ShouldBeEmpty(); // can't approve

        (await c.Owner.Client.PostAsync($"/api/v1/purchase-orders/{po}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var incoming = (await InboxAsync(receiver, "PurchaseOrderApproved")).ShouldHaveSingleItem();
        incoming.GetProperty("message").GetString()!.ShouldContain("Expected Nov 16");
        (await InboxAsync(manager, "PurchaseOrderApproved")).ShouldHaveSingleItem();
        (await InboxAsync(c.Owner, "PurchaseOrderApproved")).ShouldBeEmpty(); // they approved it
    }

    [Fact]
    public async Task Inbox_is_private_and_supports_read_state()
    {
        var c = await SetUpAsync();
        await StockOutAsync(c.Owner.Client, c.Main, c.Variant, 6);  // 14: low
        await StockOutAsync(c.Owner.Client, c.Main, c.Variant, 14); // 0: out

        (await c.Owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count", Json)).GetProperty("count").GetInt32().ShouldBe(2);
        var newest = (await InboxAsync(c.Owner))[0].GetProperty("id").GetGuid();

        // Someone else (even in the same company) can't read or mark it.
        var manager = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.ManagerId);
        (await manager.Client.PostAsync($"/api/v1/notifications/{newest}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var other = await factory.RegisterCompanyAsync();
        (await other.Client.PostAsync($"/api/v1/notifications/{newest}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await c.Owner.Client.PostAsync($"/api/v1/notifications/{newest}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unread = await c.Owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?unreadOnly=true", Json);
        unread.GetProperty("totalCount").GetInt32().ShouldBe(1);

        var all = await OkJsonAsync(await c.Owner.Client.PostAsync("/api/v1/notifications/read-all", null));
        all.GetProperty("marked").GetInt32().ShouldBe(1);
        (await c.Owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count", Json)).GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Preferences_list_only_relevant_types_and_turning_one_off_stops_it()
    {
        var c = await SetUpAsync();
        var manager = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.ManagerId);
        var sales = await factory.CreateUserAsync(c.Owner.TenantId, SystemRoles.SalesStaffId);

        var salesPrefs = await sales.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/preferences", Json);
        salesPrefs.EnumerateArray().Select(p => p.GetProperty("type").GetString()).ShouldBe(["SalesOrderReadyToShip"]);

        var updated = await OkJsonAsync(await manager.Client.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { preferences = new[] { new { type = "LowStock", inApp = false } } }));
        updated.EnumerateArray().Single(p => p.GetProperty("type").GetString() == "LowStock").GetProperty("inApp").GetBoolean().ShouldBeFalse();

        await StockOutAsync(c.Owner.Client, c.Main, c.Variant, 6); // 14: low
        (await InboxAsync(c.Owner, "LowStock")).ShouldHaveSingleItem();
        (await InboxAsync(manager, "LowStock")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Owners_hear_when_an_invitee_joins()
    {
        var owner = await factory.RegisterCompanyAsync();
        var email = TestHelpers.UniqueEmail("joiner");
        await OkJsonAsync(await owner.Client.PostAsJsonAsync("/api/v1/users",
            new { email, firstName = "Nina", lastName = "Cruz", roleId = SystemRoles.SalesStaffId, warehouseIds = Array.Empty<Guid>() }));
        var token = factory.Emails.LatestToken(email, "/accept-invitation");

        await OkJsonAsync(await factory.CreateHttpsClient().PostAsJsonAsync("/api/v1/auth/accept-invitation", new { token, password = TestHelpers.Password }));

        var joined = (await InboxAsync(owner, "UserJoined")).ShouldHaveSingleItem();
        joined.GetProperty("title").GetString().ShouldBe("Nina Cruz joined");
        joined.GetProperty("message").GetString()!.ShouldContain("Sales Staff");
    }
}
