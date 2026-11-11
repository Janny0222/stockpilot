using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Notifications;
using StockPilot.Infrastructure.Persistence;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// Live notifications over SignalR: delivered to the recipient only, only after the change commits, and the hub
/// accepts the same tokens (and only the same tokens) as the API.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(ApiFactory factory)
{
    private const string Hub = "https://localhost/api/v1/hubs/notifications";
    private static readonly TimeSpan Arrives = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    /// <summary>Connects as a user over long polling (TestServer has no real sockets) and records every push.</summary>
    private async Task<(HubConnection Connection, ConcurrentQueue<JsonElement> Received)> ConnectAsync(TestSession who)
    {
        var received = new ConcurrentQueue<JsonElement>();
        var connection = new HubConnectionBuilder()
            .WithUrl(Hub, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(who.AccessToken);
            })
            .Build();
        connection.On<JsonElement>("notification", n => received.Enqueue(n));
        await connection.StartAsync();
        return (connection, received);
    }

    private static async Task<JsonElement> NextAsync(ConcurrentQueue<JsonElement> queue)
    {
        var until = DateTime.UtcNow + Arrives;
        while (DateTime.UtcNow < until)
        {
            if (queue.TryDequeue(out var item)) return item;
            await Task.Delay(50);
        }

        throw new TimeoutException("No live notification arrived.");
    }

    [Fact]
    public async Task A_committed_notification_reaches_its_recipient_live_and_nobody_else()
    {
        var owner = await factory.RegisterCompanyAsync();
        var manager = await factory.CreateUserAsync(owner.TenantId, SystemRoles.ManagerId);
        var (ownerHub, ownerInbox) = await ConnectAsync(owner);
        var (managerHub, managerInbox) = await ConnectAsync(manager);
        await using var _ = ownerHub;
        await using var __ = managerHub;

        var warehouse = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var supplier = (await (await owner.Client.PostAsJsonAsync("/api/v1/suppliers", new { code = "S1", name = "Philips", paymentTermsDays = 30 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var product = await (await owner.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Bulb", unitOfMeasure = "pcs", variants = new[] { new { sku = "B-1", costPrice = 10m, sellingPrice = 15m, reorderLevel = 0m } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var po = (await (await manager.Client.PostAsJsonAsync("/api/v1/purchase-orders", new
        {
            supplierId = supplier, warehouseId = warehouse, orderDate = "2026-11-09",
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetGuid(), quantity = 5m, unitCost = 10m } },
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await manager.Client.PostAsync($"/api/v1/purchase-orders/{po}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Same shape as GET /notifications, so the client can drop it straight into its list.
        var pushed = await NextAsync(ownerInbox);
        pushed.GetProperty("type").GetString().ShouldBe("PurchaseOrderAwaitingApproval");
        pushed.GetProperty("entityId").GetGuid().ShouldBe(po);
        var stored = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        pushed.GetProperty("id").GetGuid().ShouldBe(stored.GetProperty("items")[0].GetProperty("id").GetGuid());

        await Task.Delay(Silence);
        managerInbox.ShouldBeEmpty(); // they submitted it, and pushes are per user
    }

    [Fact]
    public async Task Nothing_is_pushed_for_a_transaction_that_rolls_back()
    {
        var owner = await factory.RegisterCompanyAsync();
        var (hub, inbox) = await ConnectAsync(owner);
        await using var _ = hub;

        async Task WriteAsync(string title, bool fail)
        {
            await using var scope = factory.CreateScopeAs(owner.TenantId, owner.UserId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try
            {
                await db.ExecuteInTransactionAsync(async ct =>
                {
                    db.Notifications.Add(new Notification(owner.TenantId, owner.UserId, NotificationType.LowStock, title, "test", null, null, DateTimeOffset.UtcNow));
                    await db.SaveChangesAsync(ct); // saved inside the transaction, not yet committed
                    if (fail) throw new InvalidOperationException("Simulated failure after SaveChanges");
                    return true;
                }, CancellationToken.None);
            }
            catch (InvalidOperationException) when (fail)
            {
            }
        }

        await WriteAsync("rolled back", fail: true);
        await WriteAsync("committed", fail: false);

        (await NextAsync(inbox)).GetProperty("title").GetString().ShouldBe("committed");
        await Task.Delay(Silence);
        inbox.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_hub_needs_a_valid_token_and_query_string_tokens_work_nowhere_else()
    {
        var anonymous = factory.CreateHttpsClient();
        (await anonymous.PostAsync("/api/v1/hubs/notifications/negotiate?negotiateVersion=1", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var owner = await factory.RegisterCompanyAsync();
        (await anonymous.PostAsync($"/api/v1/hubs/notifications/negotiate?negotiateVersion=1&access_token={owner.AccessToken}", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/auth/me?access_token={owner.AccessToken}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
