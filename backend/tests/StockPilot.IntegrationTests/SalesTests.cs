using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SalesTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Setup(TestSession Owner, Guid Warehouse, Guid Customer, Guid VariantA, Guid VariantB);

    /// <summary>A company with one customer and two items, A and B, each with <paramref name="stock"/> units on hand at ₱100.</summary>
    private async Task<Setup> SetupAsync(decimal stock = 10)
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var customer = await (await owner.Client.PostAsJsonAsync("/api/v1/customers", new { code = "CUS1", name = "Customer One", paymentTermsDays = 15 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var product = await (await owner.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Bulb", unitOfMeasure = "pcs",
            variants = new[]
            {
                new { sku = "B-9W", costPrice = 100m, sellingPrice = 150m, reorderLevel = 1m },
                new { sku = "B-12W", costPrice = 100m, sellingPrice = 150m, reorderLevel = 1m },
            },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var variants = product.GetProperty("variants");
        var s = new Setup(owner, warehouse, customer.GetProperty("id").GetGuid(), variants[0].GetProperty("id").GetGuid(), variants[1].GetProperty("id").GetGuid());

        foreach (var variant in new[] { s.VariantA, s.VariantB })
        {
            (await PostIdempotentAsync(owner.Client, "/api/v1/inventory/stock-in",
                new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = stock, unitCost = 100m } } }))
                .StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        return s;
    }

    private static Task<HttpResponseMessage> PostIdempotentAsync(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    private static async Task<Guid> CreateOrderAsync(HttpClient client, Setup s, decimal qtyA = 4, decimal qtyB = 2, decimal price = 150m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/sales-orders", new
        {
            customerId = s.Customer, warehouseId = s.Warehouse, orderDate = "2026-11-09",
            lines = new[] { new { variantId = s.VariantA, quantity = qtyA, unitPrice = price }, new { variantId = s.VariantB, quantity = qtyB, unitPrice = price } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid order) => client.PostAsync($"/api/v1/sales-orders/{order}/confirm", null);

    private static Task<HttpResponseMessage> ShipAsync(HttpClient client, Guid order, params (Guid ItemId, decimal Qty)[] lines) =>
        PostIdempotentAsync(client, $"/api/v1/sales-orders/{order}/shipments",
            new { lines = lines.Select(l => new { itemId = l.ItemId, quantity = l.Qty }), note = "Truck 1" });

    private static async Task<JsonElement> GetOrderAsync(HttpClient client, Guid order) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/sales-orders/{order}", Json);

    private static Guid Item(JsonElement order, string sku) =>
        order.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sku").GetString() == sku).GetProperty("id").GetGuid();

    private Task<WarehouseStock> StockAsync(Setup s, Guid variant) =>
        factory.WithDbAsync(db => db.WarehouseStocks.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.WarehouseId == s.Warehouse && x.ProductVariantId == variant));

    [Fact]
    public async Task Full_lifecycle_reserves_on_confirm_and_ships_out_of_the_reservation()
    {
        var s = await SetupAsync(stock: 10);
        var order = await CreateOrderAsync(s.Owner.Client, s, qtyA: 6, qtyB: 2);

        var draft = await GetOrderAsync(s.Owner.Client, order);
        draft.GetProperty("number").GetString().ShouldBe("SO-000001");
        draft.GetProperty("total").GetDecimal().ShouldBe(1200m); // (6 + 2) × 150
        (await StockAsync(s, s.VariantA)).QuantityReserved.ShouldBe(0); // a draft promises nothing

        (await ConfirmAsync(s.Owner.Client, order)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var reserved = await StockAsync(s, s.VariantA);
        reserved.QuantityOnHand.ShouldBe(10);
        reserved.QuantityReserved.ShouldBe(6);

        var confirmed = await GetOrderAsync(s.Owner.Client, order);
        var first = await ShipAsync(s.Owner.Client, order, (Item(confirmed, "B-9W"), 4));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstResult.GetProperty("shipmentNumber").GetString().ShouldBe("DR-000001");
        firstResult.GetProperty("status").GetString().ShouldBe("PartiallyFulfilled");

        var afterFirst = await StockAsync(s, s.VariantA);
        afterFirst.QuantityOnHand.ShouldBe(6);
        afterFirst.QuantityReserved.ShouldBe(2);

        var second = await ShipAsync(s.Owner.Client, order, (Item(confirmed, "B-9W"), 2), (Item(confirmed, "B-12W"), 2));
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("Fulfilled");
        (await StockAsync(s, s.VariantA)).QuantityReserved.ShouldBe(0);
        (await StockAsync(s, s.VariantB)).QuantityOnHand.ShouldBe(8);

        var done = await GetOrderAsync(s.Owner.Client, order);
        done.GetProperty("shipments").GetArrayLength().ShouldBe(2);
        var profit = done.GetProperty("profit");
        profit.GetProperty("revenue").GetDecimal().ShouldBe(1200m);
        profit.GetProperty("costOfGoods").GetDecimal().ShouldBe(800m); // 8 × ₱100 average cost
        profit.GetProperty("grossProfit").GetDecimal().ShouldBe(400m);

        var ledger = await s.Owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/movements?type=SalesFulfilment", Json);
        ledger.GetProperty("totalCount").GetInt32().ShouldBe(3);
        ledger.GetProperty("items")[0].GetProperty("reference").GetString()!.ShouldStartWith("SO-000001 / DR-");
        ledger.GetProperty("items")[0].GetProperty("quantityChange").GetDecimal().ShouldBeLessThan(0);
    }

    [Fact]
    public async Task Confirming_more_than_is_available_fails_and_reserves_nothing()
    {
        var s = await SetupAsync(stock: 5);
        var order = await CreateOrderAsync(s.Owner.Client, s, qtyA: 3, qtyB: 9); // A fits, B doesn't

        var response = await ConfirmAsync(s.Owner.Client, order);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.Code.ShouldBe("INSUFFICIENT_STOCK");
        (await GetOrderAsync(s.Owner.Client, order)).GetProperty("status").GetString().ShouldBe("Draft");
        (await StockAsync(s, s.VariantA)).QuantityReserved.ShouldBe(0); // all-or-nothing: A wasn't reserved either
    }

    /// <summary>Five orders compete for the last 10 units, 4 each: only two can be confirmed.</summary>
    [Fact]
    public async Task Concurrent_confirmations_never_reserve_more_than_is_on_hand()
    {
        var s = await SetupAsync(stock: 10);
        var orders = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            orders.Add(await CreateOrderAsync(s.Owner.Client, s, qtyA: 4, qtyB: 1));
        }

        var responses = await Task.WhenAll(orders.Select(o => ConfirmAsync(s.Owner.Client, o)));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(2);
        responses.Where(r => r.StatusCode != HttpStatusCode.NoContent).ShouldAllBe(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        (await StockAsync(s, s.VariantA)).QuantityReserved.ShouldBe(8);
    }

    [Fact]
    public async Task Reserved_stock_cannot_be_taken_by_a_manual_stock_out()
    {
        var s = await SetupAsync(stock: 10);
        var order = await CreateOrderAsync(s.Owner.Client, s, qtyA: 8, qtyB: 1);
        (await ConfirmAsync(s.Owner.Client, order)).EnsureSuccessStatusCode();

        var stockOut = await PostIdempotentAsync(s.Owner.Client, "/api/v1/inventory/stock-out",
            new { warehouseId = s.Warehouse, lines = new[] { new { variantId = s.VariantA, quantity = 3m } } });

        stockOut.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await stockOut.ReadProblemAsync()).Code.ShouldBe("INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task Concurrent_shipments_of_the_same_order_never_over_ship()
    {
        var s = await SetupAsync(stock: 10);
        var order = await CreateOrderAsync(s.Owner.Client, s, qtyA: 10, qtyB: 1);
        (await ConfirmAsync(s.Owner.Client, order)).EnsureSuccessStatusCode();
        var item = Item(await GetOrderAsync(s.Owner.Client, order), "B-9W");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ShipAsync(s.Owner.Client, order, (item, 10))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var stock = await StockAsync(s, s.VariantA);
        stock.QuantityOnHand.ShouldBe(0);
        stock.QuantityReserved.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelling_a_partly_shipped_order_releases_the_rest()
    {
        var s = await SetupAsync(stock: 10);
        var order = await CreateOrderAsync(s.Owner.Client, s, qtyA: 6, qtyB: 1);
        (await ConfirmAsync(s.Owner.Client, order)).EnsureSuccessStatusCode();
        (await ShipAsync(s.Owner.Client, order, (Item(await GetOrderAsync(s.Owner.Client, order), "B-9W"), 2))).EnsureSuccessStatusCode();

        (await s.Owner.Client.PostAsJsonAsync($"/api/v1/sales-orders/{order}/cancel", new { reason = "Customer reduced the order" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetOrderAsync(s.Owner.Client, order)).GetProperty("status").GetString().ShouldBe("Cancelled");
        var stock = await StockAsync(s, s.VariantA);
        stock.QuantityOnHand.ShouldBe(8);   // the 2 shipped stay shipped
        stock.QuantityReserved.ShouldBe(0); // the 4 not shipped are available again
        (await StockAsync(s, s.VariantB)).QuantityReserved.ShouldBe(0);
    }

    [Fact]
    public async Task Status_rules_and_draft_edit_concurrency_are_enforced()
    {
        var s = await SetupAsync();
        var order = await CreateOrderAsync(s.Owner.Client, s);
        var draft = await GetOrderAsync(s.Owner.Client, order);

        var tooEarly = await ShipAsync(s.Owner.Client, order, (Item(draft, "B-9W"), 1));
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await tooEarly.ReadProblemAsync()).Code.ShouldBe("INVALID_STATUS");

        var version = draft.GetProperty("version").GetUInt32();
        object Edit(decimal qty) => new
        {
            customerId = s.Customer, warehouseId = s.Warehouse, orderDate = "2026-11-09", version,
            lines = new[] { new { variantId = s.VariantA, quantity = qty, unitPrice = 200m } },
        };
        (await s.Owner.Client.PutAsJsonAsync($"/api/v1/sales-orders/{order}", Edit(3))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetOrderAsync(s.Owner.Client, order)).GetProperty("total").GetDecimal().ShouldBe(600m);
        var stale = await s.Owner.Client.PutAsJsonAsync($"/api/v1/sales-orders/{order}", Edit(5));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await ConfirmAsync(s.Owner.Client, order)).EnsureSuccessStatusCode();
        (await ConfirmAsync(s.Owner.Client, order)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var current = (await GetOrderAsync(s.Owner.Client, order)).GetProperty("version").GetUInt32();
        (await s.Owner.Client.PutAsJsonAsync($"/api/v1/sales-orders/{order}", new
        {
            customerId = s.Customer, warehouseId = s.Warehouse, orderDate = "2026-11-09", version = current,
            lines = new[] { new { variantId = s.VariantA, quantity = 1m, unitPrice = 1m } },
        })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Roles_sales_staff_sell_but_cannot_cancel_or_see_cost_and_inventory_staff_only_ship_their_warehouse()
    {
        var s = await SetupAsync();
        var sales = await factory.CreateUserAsync(s.Owner.TenantId, SystemRoles.SalesStaffId);
        var otherWarehouse = await (await s.Owner.Client.PostAsJsonAsync("/api/v1/warehouses", new { code = "CEB", name = "Cebu" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var staffElsewhere = await factory.CreateUserAsync(s.Owner.TenantId, SystemRoles.InventoryStaffId, otherWarehouse.GetProperty("id").GetGuid());
        var staffHere = await factory.CreateUserAsync(s.Owner.TenantId, SystemRoles.InventoryStaffId, s.Warehouse);

        var order = await CreateOrderAsync(sales.Client, s);
        (await ConfirmAsync(sales.Client, order)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var seen = await GetOrderAsync(sales.Client, order);
        seen.GetProperty("total").GetDecimal().ShouldBe(900m);                    // prices are visible to sales
        seen.GetProperty("profit").ValueKind.ShouldBe(JsonValueKind.Null);        // cost and margin are not
        (await sales.Client.PostAsJsonAsync($"/api/v1/sales-orders/{order}/cancel", new { reason = "x" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await staffHere.Client.PostAsJsonAsync("/api/v1/sales-orders", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ShipAsync(staffElsewhere.Client, order, (Item(seen, "B-9W"), 1))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ShipAsync(staffHere.Client, order, (Item(seen, "B-9W"), 1))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Another_companys_customer_cannot_be_used_and_their_orders_are_invisible()
    {
        var s = await SetupAsync();
        var other = await SetupAsync();
        var theirs = await CreateOrderAsync(other.Owner.Client, other);

        (await s.Owner.Client.GetAsync($"/api/v1/sales-orders/{theirs}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ConfirmAsync(s.Owner.Client, theirs)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var withTheirCustomer = await s.Owner.Client.PostAsJsonAsync("/api/v1/sales-orders", new
        {
            customerId = other.Customer, warehouseId = s.Warehouse, orderDate = "2026-11-09",
            lines = new[] { new { variantId = s.VariantA, quantity = 1m, unitPrice = 1m } },
        });
        withTheirCustomer.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await withTheirCustomer.ReadProblemAsync()).Code.ShouldBe("CUSTOMER_NOT_FOUND");

        var duplicate = await s.Owner.Client.PostAsJsonAsync("/api/v1/customers", new { code = "cus1", name = "Copy", paymentTermsDays = 0 });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ReadProblemAsync()).Code.ShouldBe("CUSTOMER_CODE_TAKEN");
    }
}
