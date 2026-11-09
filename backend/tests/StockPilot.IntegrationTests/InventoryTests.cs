using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// The stock engine. These run against real PostgreSQL because the guarantees under test (row locks,
/// transactions, unique indexes) only exist in a real database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InventoryTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------- helpers ----------------

    private static async Task<Guid> CreateVariantAsync(HttpClient client, string sku, decimal cost = 50m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            name = $"Item {sku}", unitOfMeasure = "pcs",
            variants = new[] { new { sku, costPrice = cost, sellingPrice = cost * 2, reorderLevel = 5m } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variants")[0].GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateWarehouseAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/v1/warehouses", new { code, name = $"Warehouse {code}" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> StockIn(HttpClient c, Guid warehouse, Guid variant, decimal qty, decimal? cost = null, string? key = null) =>
        PostAsync(c, "/api/v1/inventory/stock-in", new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = qty, unitCost = cost } } }, key);

    private static Task<HttpResponseMessage> StockOut(HttpClient c, Guid warehouse, Guid variant, decimal qty, string? key = null) =>
        PostAsync(c, "/api/v1/inventory/stock-out", new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = qty } } }, key);

    private Task<(decimal OnHand, decimal AvgCost)> BalanceAsync(Guid warehouse, Guid variant) =>
        factory.WithDbAsync(async db =>
        {
            var s = await db.WarehouseStocks.IgnoreQueryFilters().SingleAsync(x => x.WarehouseId == warehouse && x.ProductVariantId == variant);
            return (s.QuantityOnHand, s.AverageUnitCost);
        });

    // ---------------- tests ----------------

    [Fact]
    public async Task Stock_in_updates_balance_average_cost_and_ledger()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var variant = await CreateVariantAsync(owner.Client, "IN-1");

        (await StockIn(owner.Client, warehouse, variant, 10, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await StockIn(owner.Client, warehouse, variant, 30, 120m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await BalanceAsync(warehouse, variant)).ShouldBe((40m, 115m));

        var ledger = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/movements?variantId={variant}", Json);
        ledger.GetProperty("totalCount").GetInt32().ShouldBe(2);
        ledger.GetProperty("items")[0].GetProperty("quantityAfter").GetDecimal().ShouldBe(40m); // newest first
    }

    [Fact]
    public async Task A_failing_line_rolls_back_the_whole_stock_out()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var plenty = await CreateVariantAsync(owner.Client, "PLENTY");
        var scarce = await CreateVariantAsync(owner.Client, "SCARCE");
        await StockIn(owner.Client, warehouse, plenty, 100);
        await StockIn(owner.Client, warehouse, scarce, 2);

        var response = await PostAsync(owner.Client, "/api/v1/inventory/stock-out", new
        {
            warehouseId = warehouse,
            lines = new[] { new { variantId = plenty, quantity = 10m }, new { variantId = scarce, quantity = 5m } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.ShouldBe("INSUFFICIENT_STOCK");
        (await BalanceAsync(warehouse, plenty)).OnHand.ShouldBe(100m); // line 1 was NOT applied
    }

    /// <summary>
    /// THE overselling test. 50 requests race to take 1 unit each from a stock of 10.
    /// Without row locks, several requests read "10 available" at the same moment and all succeed.
    /// With SELECT … FOR UPDATE, they queue, and exactly 10 win.
    /// </summary>
    [Fact]
    public async Task Fifty_concurrent_stock_outs_never_oversell()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var variant = await CreateVariantAsync(owner.Client, "HOT-ITEM");
        await StockIn(owner.Client, warehouse, variant, 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => StockOut(owner.Client, warehouse, variant, 1)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(10);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(40);
        (await BalanceAsync(warehouse, variant)).OnHand.ShouldBe(0m);
        await AssertLedgerReconcilesAsync(owner.TenantId);
    }

    [Fact]
    public async Task Concurrent_transfers_in_opposite_directions_do_not_deadlock_and_conserve_stock()
    {
        var owner = await factory.RegisterCompanyAsync();
        var a = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var b = await CreateWarehouseAsync(owner.Client, "B");
        var v1 = await CreateVariantAsync(owner.Client, "X-1");
        var v2 = await CreateVariantAsync(owner.Client, "X-2");
        foreach (var w in new[] { a, b })
        {
            foreach (var v in new[] { v1, v2 })
            {
                await StockIn(owner.Client, w, v, 100);
            }
        }

        var lines = new[] { new { variantId = v1, quantity = 1m }, new { variantId = v2, quantity = 1m } };
        var transfers = Enumerable.Range(0, 30).Select(i => PostAsync(owner.Client, "/api/v1/transfers",
            new { sourceWarehouseId = i % 2 == 0 ? a : b, destinationWarehouseId = i % 2 == 0 ? b : a, lines }));

        var responses = await Task.WhenAll(transfers);

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        ((await BalanceAsync(a, v1)).OnHand + (await BalanceAsync(b, v1)).OnHand).ShouldBe(200m);
        ((await BalanceAsync(a, v2)).OnHand + (await BalanceAsync(b, v2)).OnHand).ShouldBe(200m);
        await AssertLedgerReconcilesAsync(owner.TenantId);
    }

    [Fact]
    public async Task Transfer_moves_stock_at_source_cost_and_numbers_documents_sequentially()
    {
        var owner = await factory.RegisterCompanyAsync();
        var a = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var b = await CreateWarehouseAsync(owner.Client, "B");
        var variant = await CreateVariantAsync(owner.Client, "TR-ITEM");
        await StockIn(owner.Client, a, variant, 20, 75m);

        var first = await PostAsync(owner.Client, "/api/v1/transfers", new { sourceWarehouseId = a, destinationWarehouseId = b, lines = new[] { new { variantId = variant, quantity = 5m } } });
        var second = await PostAsync(owner.Client, "/api/v1/transfers", new { sourceWarehouseId = a, destinationWarehouseId = b, lines = new[] { new { variantId = variant, quantity = 3m } } });

        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentNumber").GetString().ShouldBe("TR-000001");
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentNumber").GetString().ShouldBe("TR-000002");
        (await BalanceAsync(a, variant)).OnHand.ShouldBe(12m);
        (await BalanceAsync(b, variant)).ShouldBe((8m, 75m));

        var same = await PostAsync(owner.Client, "/api/v1/transfers", new { sourceWarehouseId = a, destinationWarehouseId = a, lines = new[] { new { variantId = variant, quantity = 1m } } });
        same.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Idempotency_key_makes_a_retried_request_count_once()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var variant = await CreateVariantAsync(owner.Client, "IDEMP");
        await StockIn(owner.Client, warehouse, variant, 10);

        var first = await StockOut(owner.Client, warehouse, variant, 4, key: "double-click-123");
        var replay = await StockOut(owner.Client, warehouse, variant, 4, key: "double-click-123");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await BalanceAsync(warehouse, variant)).OnHand.ShouldBe(6m); // NOT 2

        var reused = await StockOut(owner.Client, warehouse, variant, 1, key: "double-click-123");
        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reused.ReadProblemAsync()).Code.ShouldBe("IDEMPOTENCY_KEY_REUSED");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/inventory/stock-out")
        {
            Content = JsonContent.Create(new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = 1m } } }),
        };
        var missingKey = await owner.Client.SendAsync(request);
        missingKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await missingKey.ReadProblemAsync()).Code.ShouldBe("IDEMPOTENCY_KEY_REQUIRED");
    }

    [Fact]
    public async Task A_failed_request_releases_its_idempotency_key_for_a_retry()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var variant = await CreateVariantAsync(owner.Client, "RETRY");

        (await StockOut(owner.Client, warehouse, variant, 3, key: "retry-key")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await StockIn(owner.Client, warehouse, variant, 5);

        (await StockOut(owner.Client, warehouse, variant, 3, key: "retry-key")).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Adjustment_records_the_difference_with_a_reason_and_needs_permission()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var variant = await CreateVariantAsync(owner.Client, "COUNT");
        await StockIn(owner.Client, warehouse, variant, 10);

        var response = await PostAsync(owner.Client, "/api/v1/inventory/adjustments",
            new { warehouseId = warehouse, variantId = variant, countedQuantity = 7m, reason = "Damaged", note = "Crushed box" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var movement = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movements")[0];
        movement.GetProperty("type").GetString().ShouldBe("AdjustmentDecrease");
        movement.GetProperty("quantityChange").GetDecimal().ShouldBe(-3m);

        var staff = await factory.CreateUserAsync(owner.TenantId, SystemRoles.InventoryStaffId, warehouse);
        (await PostAsync(staff.Client, "/api/v1/inventory/adjustments", new { warehouseId = warehouse, variantId = variant, countedQuantity = 1m, reason = "Lost" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Inventory_staff_can_only_move_stock_in_assigned_warehouses()
    {
        var owner = await factory.RegisterCompanyAsync();
        var assigned = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var other = await CreateWarehouseAsync(owner.Client, "OTHER");
        var variant = await CreateVariantAsync(owner.Client, "SCOPED");
        await StockIn(owner.Client, assigned, variant, 10);
        await StockIn(owner.Client, other, variant, 10);
        var staff = await factory.CreateUserAsync(owner.TenantId, SystemRoles.InventoryStaffId, assigned);

        (await StockOut(staff.Client, assigned, variant, 1)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await StockOut(staff.Client, other, variant, 1)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(staff.Client, "/api/v1/transfers", new { sourceWarehouseId = assigned, destinationWarehouseId = other, lines = new[] { new { variantId = variant, quantity = 1m } } }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var visible = await staff.Client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/stock", Json);
        visible.GetProperty("items").EnumerateArray().ShouldAllBe(i => i.GetProperty("warehouseId").GetGuid() == assigned);
    }

    [Fact]
    public async Task Another_companys_items_and_warehouses_cannot_be_used()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();
        var acmeWarehouse = await factory.DefaultWarehouseIdAsync(acme.TenantId);
        var globexWarehouse = await factory.DefaultWarehouseIdAsync(globex.TenantId);
        var globexVariant = await CreateVariantAsync(globex.Client, "THEIRS");

        var theirItem = await StockIn(acme.Client, acmeWarehouse, globexVariant, 5);
        theirItem.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await theirItem.ReadProblemAsync()).Code.ShouldBe("VARIANT_NOT_FOUND");

        var acmeVariant = await CreateVariantAsync(acme.Client, "MINE");
        (await StockIn(acme.Client, globexWarehouse, acmeVariant, 5)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sales_staff_see_stock_quantities_but_not_costs()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var variant = await CreateVariantAsync(owner.Client, "PRICED");
        await StockIn(owner.Client, warehouse, variant, 10, 80m);
        var sales = await factory.CreateUserAsync(owner.TenantId, SystemRoles.SalesStaffId);

        var row = (await sales.Client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/stock", Json)).GetProperty("items")[0];

        row.GetProperty("available").GetDecimal().ShouldBe(10m);
        row.GetProperty("averageUnitCost").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("stockValue").ValueKind.ShouldBe(JsonValueKind.Null);
        (await StockIn(sales.Client, warehouse, variant, 1)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Warehouses_with_stock_or_the_default_cannot_be_archived()
    {
        var owner = await factory.RegisterCompanyAsync();
        var main = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var spare = await CreateWarehouseAsync(owner.Client, "SPARE");
        var variant = await CreateVariantAsync(owner.Client, "HELD");
        await StockIn(owner.Client, spare, variant, 1);

        var withStock = await owner.Client.PostAsync($"/api/v1/warehouses/{spare}/archive", null);
        withStock.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await withStock.ReadProblemAsync()).Code.ShouldBe("WAREHOUSE_HAS_STOCK");

        var isDefault = await owner.Client.PostAsync($"/api/v1/warehouses/{main}/archive", null);
        (await isDefault.ReadProblemAsync()).Code.ShouldBe("WAREHOUSE_IS_DEFAULT");

        (await owner.Client.PostAsync($"/api/v1/warehouses/{spare}/default", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.Client.PostAsync($"/api/v1/warehouses/{main}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>The ledger is the source of truth: for every balance row, SUM(movements) must equal on-hand.</summary>
    private async Task AssertLedgerReconcilesAsync(Guid tenantId)
    {
        var mismatches = await factory.WithDbAsync(async db =>
        {
            var balances = await db.WarehouseStocks.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ToListAsync();
            var sums = await db.InventoryTransactions.IgnoreQueryFilters().Where(t => t.TenantId == tenantId)
                .GroupBy(t => new { t.WarehouseId, t.ProductVariantId })
                .Select(g => new { g.Key.WarehouseId, g.Key.ProductVariantId, Total = g.Sum(t => t.QuantityChange) })
                .ToListAsync();
            return balances.Count(b => (sums.SingleOrDefault(s => s.WarehouseId == b.WarehouseId && s.ProductVariantId == b.ProductVariantId)?.Total ?? 0) != b.QuantityOnHand);
        });

        mismatches.ShouldBe(0);
    }
}
