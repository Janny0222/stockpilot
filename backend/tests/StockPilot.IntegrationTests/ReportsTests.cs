using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// Dashboard and reports must show REAL numbers. Each test builds a small, fully known history through the public
/// API (stock-in, purchase receipt, sales shipment) and checks the aggregates against hand-computed values.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReportsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record World(TestSession Owner, Guid Warehouse, Guid Customer, Guid Variant, Guid Order);

    private static async Task<HttpResponseMessage> PostIdempotentAsync(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return response;
    }

    /// <summary>
    /// 20 units in at ₱100. A sales order for 8 at ₱150, confirmed, 6 shipped today.
    /// → revenue 900, COGS 600, profit 300 (33.3%); on hand 14 worth 1,400; 2 still reserved.
    /// </summary>
    private async Task<World> BuildAsync(string productName = "Bulb")
    {
        var owner = await factory.RegisterCompanyAsync();
        var c = owner.Client;
        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var customer = (await (await c.PostAsJsonAsync("/api/v1/customers", new { code = "C1", name = "Customer One", paymentTermsDays = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var product = await (await c.PostAsJsonAsync("/api/v1/products", new
        {
            name = productName, unitOfMeasure = "pcs",
            variants = new[] { new { sku = "B-1", costPrice = 100m, sellingPrice = 150m, reorderLevel = 15m } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var variant = product.GetProperty("variants")[0].GetProperty("id").GetGuid();

        await PostIdempotentAsync(c, "/api/v1/inventory/stock-in", new { warehouseId = warehouse, lines = new[] { new { variantId = variant, quantity = 20m, unitCost = 100m } } });

        var order = (await (await c.PostAsJsonAsync("/api/v1/sales-orders", new
        {
            customerId = customer, warehouseId = warehouse, orderDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            lines = new[] { new { variantId = variant, quantity = 8m, unitPrice = 150m } },
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await c.PostAsync($"/api/v1/sales-orders/{order}/confirm", null)).EnsureSuccessStatusCode();
        var item = (await c.GetFromJsonAsync<JsonElement>($"/api/v1/sales-orders/{order}", Json)).GetProperty("items")[0].GetProperty("id").GetGuid();
        await PostIdempotentAsync(c, $"/api/v1/sales-orders/{order}/shipments", new { lines = new[] { new { itemId = item, quantity = 6m } } });

        return new World(owner, warehouse, customer, variant, order);
    }

    [Fact]
    public async Task Dashboard_aggregates_match_the_actual_transactions()
    {
        var w = await BuildAsync();

        var d = await w.Owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard?days=7", Json);

        var stock = d.GetProperty("stock");
        stock.GetProperty("units").GetDecimal().ShouldBe(14m);
        stock.GetProperty("value").GetDecimal().ShouldBe(1400m);
        stock.GetProperty("reservedUnits").GetDecimal().ShouldBe(2m);
        stock.GetProperty("lowStock").GetInt32().ShouldBe(1); // available 12 ≤ reorder level 15

        var sales = d.GetProperty("sales");
        sales.GetProperty("revenue").GetDecimal().ShouldBe(900m);
        sales.GetProperty("grossProfit").GetDecimal().ShouldBe(300m);
        sales.GetProperty("marginPercent").GetDecimal().ShouldBe(33.3m);
        sales.GetProperty("ordersShipped").GetInt32().ShouldBe(1);
        sales.GetProperty("openOrders").GetInt32().ShouldBe(1); // 2 units still to ship
        sales.GetProperty("previousRevenue").GetDecimal().ShouldBe(0m);

        var trend = d.GetProperty("salesTrend");
        trend.GetArrayLength().ShouldBe(7); // one point per day, empty days included
        trend.EnumerateArray().Sum(p => p.GetProperty("revenue").GetDecimal()).ShouldBe(900m);
        d.GetProperty("topProducts")[0].GetProperty("revenue").GetDecimal().ShouldBe(900m);
    }

    [Fact]
    public async Task Dashboard_sections_follow_the_role()
    {
        var w = await BuildAsync();
        var sales = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.SalesStaffId);
        var staff = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.InventoryStaffId, w.Warehouse);

        var forSales = await sales.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard", Json);
        forSales.GetProperty("sales").GetProperty("revenue").GetDecimal().ShouldBe(900m);
        forSales.GetProperty("sales").GetProperty("grossProfit").ValueKind.ShouldBe(JsonValueKind.Null); // no cost permission
        forSales.GetProperty("stock").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        forSales.GetProperty("purchasing").ValueKind.ShouldBe(JsonValueKind.Null);

        var forStaff = await staff.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard", Json);
        forStaff.GetProperty("stock").GetProperty("units").GetDecimal().ShouldBe(14m);
        forStaff.GetProperty("stock").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        forStaff.GetProperty("purchasing").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    /// <summary>
    /// The sales report groups in the database (performance pass). The tricky number is "orders": one order with two
    /// products counts once in EACH product's row, but only once in the total, never twice.
    /// </summary>
    [Fact]
    public async Task Sales_report_groups_match_and_one_order_counts_once_in_the_total()
    {
        var w = await BuildAsync();
        var c = w.Owner.Client;
        var second = (await (await c.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Switch", unitOfMeasure = "pcs", variants = new[] { new { sku = "S-1", costPrice = 40m, sellingPrice = 70m, reorderLevel = 0m } },
        })).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("variants")[0].GetProperty("id").GetGuid();
        await PostIdempotentAsync(c, "/api/v1/inventory/stock-in", new { warehouseId = w.Warehouse, lines = new[] { new { variantId = second, quantity = 10m, unitCost = 40m } } });

        // One order, two products, fully shipped: bulb 2 × 150, switch 3 × 70.
        var order = (await (await c.PostAsJsonAsync("/api/v1/sales-orders", new
        {
            customerId = w.Customer, warehouseId = w.Warehouse, orderDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            lines = new[] { new { variantId = w.Variant, quantity = 2m, unitPrice = 150m }, new { variantId = second, quantity = 3m, unitPrice = 70m } },
        })).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
        (await c.PostAsync($"/api/v1/sales-orders/{order}/confirm", null)).EnsureSuccessStatusCode();
        var items = (await c.GetFromJsonAsync<JsonElement>($"/api/v1/sales-orders/{order}", Json)).GetProperty("items").EnumerateArray()
            .Select(i => new { itemId = i.GetProperty("id").GetGuid(), quantity = i.GetProperty("quantityOrdered").GetDecimal() }).ToArray();
        await PostIdempotentAsync(c, $"/api/v1/sales-orders/{order}/shipments", new { lines = items });

        var report = await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales?groupBy=product", Json);
        var cols = report.GetProperty("columns").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToList();
        var rows = report.GetProperty("rows").EnumerateArray().ToDictionary(r => r[0].GetString()!.Split(' ')[0], r => r);

        // Bulb: the first order (6 × 150) + this one (2 × 150) = 2 orders, 1,200. Switch: this order only.
        rows["Bulb"][cols.IndexOf("orders")].GetInt32().ShouldBe(2);
        rows["Bulb"][cols.IndexOf("revenue")].GetDecimal().ShouldBe(1200m);
        rows["Switch"][cols.IndexOf("orders")].GetInt32().ShouldBe(1);
        rows["Switch"][cols.IndexOf("revenue")].GetDecimal().ShouldBe(210m);

        var totals = report.GetProperty("totals");
        totals[cols.IndexOf("orders")].GetInt32().ShouldBe(2); // 2 distinct orders, not 2 + 1
        totals[cols.IndexOf("revenue")].GetDecimal().ShouldBe(1410m);
        totals[cols.IndexOf("cost")].GetDecimal().ShouldBe(800m + 120m); // (6 + 2) × 100 + 3 × 40

        var byDay = await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales?groupBy=day", Json);
        byDay.GetProperty("totals")[cols.IndexOf("revenue")].GetDecimal().ShouldBe(1410m);
        byDay.GetProperty("totals")[cols.IndexOf("orders")].GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Reports_agree_with_the_dashboard_and_respect_warehouse_scope()
    {
        var w = await BuildAsync();
        var c = w.Owner.Client;

        var salesReport = await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales?groupBy=customer", Json);
        var totals = salesReport.GetProperty("totals");
        var columns = salesReport.GetProperty("columns").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToList();
        totals[columns.IndexOf("revenue")].GetDecimal().ShouldBe(900m);
        totals[columns.IndexOf("cost")].GetDecimal().ShouldBe(600m);
        salesReport.GetProperty("rows")[0][0].GetString().ShouldBe("Customer One");

        var valuation = await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/inventory-valuation", Json);
        var vCols = valuation.GetProperty("columns").EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToList();
        valuation.GetProperty("totals")[vCols.IndexOf("value")].GetDecimal().ShouldBe(1400m);
        valuation.GetProperty("totals")[vCols.IndexOf("reserved")].GetDecimal().ShouldBe(2m);

        var low = await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/low-stock", Json);
        low.GetProperty("rows").GetArrayLength().ShouldBe(1);

        var movements = await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/stock-movements", Json);
        movements.GetProperty("rows").GetArrayLength().ShouldBe(2); // stock-in + one shipment line

        // A warehouse the user can't see (here: another company's) → an empty report, never its numbers.
        var other = await BuildAsync();
        var foreign = await c.GetFromJsonAsync<JsonElement>($"/api/v1/reports/inventory-valuation?warehouseId={other.Warehouse}", Json);
        foreign.GetProperty("rows").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Sales_staff_see_no_cost_columns_and_cannot_open_reports()
    {
        var w = await BuildAsync();
        var sales = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.SalesStaffId);

        (await sales.Client.GetAsync("/api/v1/reports/sales")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await w.Owner.Client.GetAsync("/api/v1/reports/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var badPeriod = await w.Owner.Client.GetAsync("/api/v1/reports/sales?from=2026-11-10&to=2026-11-01");
        badPeriod.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await badPeriod.ReadProblemAsync()).Code.ShouldBe("PERIOD_INVALID");
    }

    [Fact]
    public async Task Csv_export_is_excel_friendly_and_neutralises_formulas()
    {
        var w = await BuildAsync(productName: "=HYPERLINK(\"http://evil.example\",\"Click\")");

        var response = await w.Owner.Client.GetAsync("/api/v1/reports/inventory-valuation/csv");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldStartWith("inventory-valuation-");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble()); // BOM for Excel
        var text = Encoding.UTF8.GetString(bytes[3..]);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldStartWith("Warehouse,SKU,Product,Unit,On hand");
        lines[1].ShouldContain("\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"Click\"\")\""); // quoted AND prefixed with '
        lines[^1].ShouldStartWith("Total,");

        var manager = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.ManagerId);
        (await manager.Client.GetAsync("/api/v1/reports/sales/csv")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var staff = await factory.CreateUserAsync(w.Owner.TenantId, SystemRoles.InventoryStaffId, w.Warehouse);
        (await staff.Client.GetAsync("/api/v1/reports/sales/csv")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
