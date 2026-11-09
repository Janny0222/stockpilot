using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PurchasingTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Setup(TestSession Owner, Guid Warehouse, Guid Supplier, Guid VariantA, Guid VariantB);

    private async Task<Setup> SetupAsync()
    {
        var owner = await factory.RegisterCompanyAsync();
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var supplier = await (await owner.Client.PostAsJsonAsync("/api/v1/suppliers", new { code = "SUP1", name = "Supplier One", paymentTermsDays = 30 }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var product = await (await owner.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Wire", unitOfMeasure = "roll",
            variants = new[]
            {
                new { sku = "W-RED", costPrice = 100m, sellingPrice = 150m, reorderLevel = 1m },
                new { sku = "W-BLK", costPrice = 100m, sellingPrice = 150m, reorderLevel = 1m },
            },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var variants = product.GetProperty("variants");
        return new Setup(owner, warehouse, supplier.GetProperty("id").GetGuid(), variants[0].GetProperty("id").GetGuid(), variants[1].GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CreatePoAsync(HttpClient client, Setup s, decimal qtyA = 10, decimal qtyB = 4, decimal cost = 120m)
    {
        var response = await client.PostAsJsonAsync("/api/v1/purchase-orders", new
        {
            supplierId = s.Supplier, warehouseId = s.Warehouse, orderDate = "2026-11-09", expectedDate = "2026-11-16",
            lines = new[] { new { variantId = s.VariantA, quantity = qtyA, unitCost = cost }, new { variantId = s.VariantB, quantity = qtyB, unitCost = cost } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, Guid po, params (Guid ItemId, decimal Qty)[] lines)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/purchase-orders/{po}/receipts")
        {
            Content = JsonContent.Create(new { lines = lines.Select(l => new { itemId = l.ItemId, quantity = l.Qty }), note = "Delivery" }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> GetPoAsync(HttpClient client, Guid po) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/purchase-orders/{po}", Json);

    private static async Task ApproveAsync(HttpClient client, Guid po)
    {
        (await client.PostAsync($"/api/v1/purchase-orders/{po}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync($"/api/v1/purchase-orders/{po}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static Guid ItemId(JsonElement po, int index) => po.GetProperty("items")[index].GetProperty("id").GetGuid();

    [Fact]
    public async Task Full_lifecycle_draft_to_received_updates_stock_cost_and_ledger()
    {
        var s = await SetupAsync();
        var po = await CreatePoAsync(s.Owner.Client, s);

        var draft = await GetPoAsync(s.Owner.Client, po);
        draft.GetProperty("number").GetString().ShouldBe("PO-000001");
        draft.GetProperty("status").GetString().ShouldBe("Draft");
        draft.GetProperty("total").GetDecimal().ShouldBe(1680m); // (10 + 4) × 120

        await ApproveAsync(s.Owner.Client, po);
        var approved = await GetPoAsync(s.Owner.Client, po);
        var a = approved.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sku").GetString() == "W-RED").GetProperty("id").GetGuid();
        var b = approved.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sku").GetString() == "W-BLK").GetProperty("id").GetGuid();

        var first = await ReceiveAsync(s.Owner.Client, po, (a, 6));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        firstResult.GetProperty("receiptNumber").GetString().ShouldBe("GRN-000001");
        firstResult.GetProperty("status").GetString().ShouldBe("PartiallyReceived");

        var second = await ReceiveAsync(s.Owner.Client, po, (a, 4), (b, 4));
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("Received");

        var stock = await factory.WithDbAsync(db => db.WarehouseStocks.IgnoreQueryFilters()
            .SingleAsync(x => x.WarehouseId == s.Warehouse && x.ProductVariantId == s.VariantA));
        stock.QuantityOnHand.ShouldBe(10m);
        stock.AverageUnitCost.ShouldBe(120m); // received at the PO price

        var receipts = (await GetPoAsync(s.Owner.Client, po)).GetProperty("receipts");
        receipts.GetArrayLength().ShouldBe(2);

        var ledger = await s.Owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/inventory/movements?type=PurchaseReceipt", Json);
        ledger.GetProperty("totalCount").GetInt32().ShouldBe(3);
        ledger.GetProperty("items")[0].GetProperty("reference").GetString()!.ShouldStartWith("PO-000001 / GRN-");
    }

    [Fact]
    public async Task Over_receiving_is_rejected_and_nothing_changes()
    {
        var s = await SetupAsync();
        var po = await CreatePoAsync(s.Owner.Client, s, qtyA: 5);
        await ApproveAsync(s.Owner.Client, po);
        var item = ItemId(await GetPoAsync(s.Owner.Client, po), 0);

        var response = await ReceiveAsync(s.Owner.Client, po, (item, 999));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.ShouldBe("OVER_RECEIPT");
        (await factory.WithDbAsync(db => db.WarehouseStocks.IgnoreQueryFilters().AnyAsync(x => x.WarehouseId == s.Warehouse && x.QuantityOnHand > 0)))
            .ShouldBeFalse();
    }

    /// <summary>Two people record the same delivery at the same moment: only one may succeed.</summary>
    [Fact]
    public async Task Concurrent_receipts_of_the_same_remaining_quantity_never_over_receive()
    {
        var s = await SetupAsync();
        var po = await CreatePoAsync(s.Owner.Client, s, qtyA: 10, qtyB: 1);
        await ApproveAsync(s.Owner.Client, po);
        var detail = await GetPoAsync(s.Owner.Client, po);
        var itemA = detail.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sku").GetString() == "W-RED").GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ReceiveAsync(s.Owner.Client, po, (itemA, 10))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.UnprocessableEntity);
        (await factory.WithDbAsync(db => db.WarehouseStocks.IgnoreQueryFilters().SingleAsync(x => x.WarehouseId == s.Warehouse && x.ProductVariantId == s.VariantA)))
            .QuantityOnHand.ShouldBe(10m);
    }

    [Fact]
    public async Task Status_rules_are_enforced_through_the_api()
    {
        var s = await SetupAsync();
        var po = await CreatePoAsync(s.Owner.Client, s);
        var item = ItemId(await GetPoAsync(s.Owner.Client, po), 0);

        var tooEarly = await ReceiveAsync(s.Owner.Client, po, (item, 1));
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await tooEarly.ReadProblemAsync()).Code.ShouldBe("INVALID_STATUS");

        (await s.Owner.Client.PostAsync($"/api/v1/purchase-orders/{po}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await ApproveAsync(s.Owner.Client, po);
        var editAfterApproval = await s.Owner.Client.PutAsJsonAsync($"/api/v1/purchase-orders/{po}", new
        {
            supplierId = s.Supplier, warehouseId = s.Warehouse, orderDate = "2026-11-09",
            lines = new[] { new { variantId = s.VariantA, quantity = 1m, unitCost = 1m } },
        });
        editAfterApproval.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await s.Owner.Client.PostAsJsonAsync($"/api/v1/purchase-orders/{po}/cancel", new { reason = "Supplier out of stock" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetPoAsync(s.Owner.Client, po)).GetProperty("status").GetString().ShouldBe("Cancelled");
    }

    [Fact]
    public async Task Draft_can_be_edited_with_concurrency_protection()
    {
        var s = await SetupAsync();
        var po = await CreatePoAsync(s.Owner.Client, s);
        var version = (await GetPoAsync(s.Owner.Client, po)).GetProperty("version").GetUInt32();
        object Edit(decimal qty) => new
        {
            supplierId = s.Supplier, warehouseId = s.Warehouse, orderDate = "2026-11-09", version,
            lines = new[] { new { variantId = s.VariantA, quantity = qty, unitCost = 50m } },
        };

        (await s.Owner.Client.PutAsJsonAsync($"/api/v1/purchase-orders/{po}", Edit(3))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var edited = await GetPoAsync(s.Owner.Client, po);
        edited.GetProperty("items").GetArrayLength().ShouldBe(1);
        edited.GetProperty("total").GetDecimal().ShouldBe(150m);

        var stale = await s.Owner.Client.PutAsJsonAsync($"/api/v1/purchase-orders/{po}", Edit(7));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ReadProblemAsync()).Code.ShouldBe("CONCURRENCY_CONFLICT");
    }

    [Fact]
    public async Task Roles_inventory_staff_receive_but_cannot_create_or_approve_and_sales_cannot_see()
    {
        var s = await SetupAsync();
        var po = await CreatePoAsync(s.Owner.Client, s);
        (await s.Owner.Client.PostAsync($"/api/v1/purchase-orders/{po}/submit", null)).EnsureSuccessStatusCode();

        var staff = await factory.CreateUserAsync(s.Owner.TenantId, SystemRoles.InventoryStaffId, s.Warehouse);
        var sales = await factory.CreateUserAsync(s.Owner.TenantId, SystemRoles.SalesStaffId);

        (await staff.Client.PostAsync($"/api/v1/purchase-orders/{po}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PostAsJsonAsync("/api/v1/purchase-orders", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sales.Client.GetAsync($"/api/v1/purchase-orders/{po}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await s.Owner.Client.PostAsync($"/api/v1/purchase-orders/{po}/approve", null)).EnsureSuccessStatusCode();
        var seen = await GetPoAsync(staff.Client, po);
        seen.GetProperty("total").ValueKind.ShouldBe(JsonValueKind.Null); // no cost permission
        (await ReceiveAsync(staff.Client, po, (ItemId(seen, 0), 1))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Another_companys_supplier_cannot_be_used_and_their_orders_are_invisible()
    {
        var s = await SetupAsync();
        var other = await SetupAsync();
        var theirPo = await CreatePoAsync(other.Owner.Client, other);

        (await s.Owner.Client.GetAsync($"/api/v1/purchase-orders/{theirPo}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var withTheirSupplier = await s.Owner.Client.PostAsJsonAsync("/api/v1/purchase-orders", new
        {
            supplierId = other.Supplier, warehouseId = s.Warehouse, orderDate = "2026-11-09",
            lines = new[] { new { variantId = s.VariantA, quantity = 1m, unitCost = 1m } },
        });
        withTheirSupplier.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await withTheirSupplier.ReadProblemAsync()).Code.ShouldBe("SUPPLIER_NOT_FOUND");
    }

    [Fact]
    public async Task Supplier_codes_are_unique_per_company()
    {
        var s = await SetupAsync();
        var duplicate = await s.Owner.Client.PostAsJsonAsync("/api/v1/suppliers", new { code = "sup1", name = "Copy", paymentTermsDays = 0 });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ReadProblemAsync()).Code.ShouldBe("SUPPLIER_CODE_TAKEN");
    }
}
