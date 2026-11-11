using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Notifications;

namespace StockPilot.Application.Features.Notifications;

/// <summary>Permission is who it concerns: only people whose role has it receive this type.</summary>
public sealed record NotificationTypeInfo(NotificationType Type, string Permission, string Label, string Description);

/// <summary>Every notification type, who it goes to, and how it's described on the preferences screen.</summary>
public static class NotificationCatalog
{
    public static IReadOnlyList<NotificationTypeInfo> All { get; } =
    [
        new(NotificationType.LowStock, Permissions.Purchases.Manage,
            "Low and out-of-stock items", "When an item drops to its reorder level or runs out, in the warehouses you can see."),
        new(NotificationType.PurchaseOrderAwaitingApproval, Permissions.Purchases.Approve,
            "Purchase orders to approve", "When someone submits a purchase order for approval."),
        new(NotificationType.PurchaseOrderApproved, Permissions.Purchases.Receive,
            "Incoming deliveries", "When a purchase order for one of your warehouses is approved, so you know goods are coming."),
        new(NotificationType.SalesOrderReadyToShip, Permissions.Sales.Fulfil,
            "Orders ready to ship", "When a sales order from one of your warehouses is confirmed and its stock is reserved."),
        new(NotificationType.UserJoined, Permissions.Users.Manage,
            "New team members", "When someone you invited accepts and signs in for the first time."),
    ];

    public static NotificationTypeInfo Get(NotificationType type) => All.Single(t => t.Type == type);
}

public sealed record NotificationContent(string Title, string Message, string? EntityType = null, Guid? EntityId = null);

/// <summary>
/// Adds notification rows to the CURRENT unit of work. The caller's SaveChanges commits them together with the change
/// they describe, so there is no "notified about something that rolled back" and no "committed but never notified".
/// </summary>
public sealed class NotificationPublisher(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    /// <summary>
    /// Everyone in the company whose role has the type's permission, who can see <paramref name="warehouseId"/> (when
    /// given), is active, has accepted their invitation and hasn't turned this type off. includeActor is false for
    /// workflow events: you don't need telling what you just did yourself.
    /// </summary>
    public async Task<int> ToAudienceAsync(NotificationType type, Guid? warehouseId, NotificationContent content, bool includeActor, CancellationToken ct)
    {
        if (currentUser.TenantId is not { } tenantId) return 0;

        var permission = NotificationCatalog.Get(type).Permission;
        var actor = currentUser.UserId;

        // One query. The tenant filter on Users, preferences and warehouse access keeps it inside this company.
        var recipients = await db.Users
            .Where(u => u.IsActive && u.PasswordHash != null)
            .Where(u => includeActor || u.Id != actor)
            .Where(u => db.RolePermissions.Any(rp => rp.RoleId == u.RoleId && rp.PermissionCode == permission))
            .Where(u => warehouseId == null || !u.Role.RestrictToAssignedWarehouses
                || db.UserWarehouseAccess.Any(a => a.UserId == u.Id && a.WarehouseId == warehouseId))
            .Where(u => !db.NotificationPreferences.Any(p => p.UserId == u.Id && p.Type == type && !p.InApp))
            .Select(u => u.Id)
            .ToListAsync(ct);

        Add(tenantId, type, recipients, content);
        return recipients.Count;
    }

    /// <summary>Specific people, already filtered by the caller (used where there's no signed-in tenant yet).</summary>
    public void Add(Guid tenantId, NotificationType type, IEnumerable<Guid> recipients, NotificationContent content)
    {
        var now = clock.GetUtcNow();
        db.Notifications.AddRange(recipients.Distinct().Select(userId =>
            new Notification(tenantId, userId, type, content.Title, content.Message, content.EntityType, content.EntityId, now)));
    }
}

/// <summary>
/// Raises a low-stock notification when a change makes an item WORSE: in stock → low, or anything → out. Staying low
/// (another sale of an item that's already low) doesn't repeat it, so people get one alert per problem, not one per
/// sale. Call it after changing the locked stock rows and before SaveChanges.
/// </summary>
public sealed class LowStockAlerts(IAppDbContext db, NotificationPublisher publisher)
{
    private const int ListedItems = 5;

    public async Task CheckAsync(IEnumerable<WarehouseStock> stocks, CancellationToken ct)
    {
        var dropped = stocks
            .Select(s =>
            {
                var entry = db.Entry(s);
                var before = entry.Property(x => x.QuantityOnHand).OriginalValue - entry.Property(x => x.QuantityReserved).OriginalValue;
                return (Stock: s, Before: before, After: s.QuantityAvailable);
            })
            .Where(x => x.After < x.Before)
            .ToList();
        if (dropped.Count == 0) return;

        var variantIds = dropped.Select(d => d.Stock.ProductVariantId).Distinct().ToList();
        var variants = await db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Join(db.Products, v => v.ProductId, p => p.Id, (v, p) => new { v.Id, v.Sku, v.Name, Product = p.Name, v.ReorderLevel })
            .ToDictionaryAsync(v => v.Id, ct);

        foreach (var group in dropped.GroupBy(d => d.Stock.WarehouseId))
        {
            var worse = group
                .Select(d => (d, v: variants[d.Stock.ProductVariantId]))
                .Where(x => Rank(x.d.After, x.v.ReorderLevel) > Rank(x.d.Before, x.v.ReorderLevel))
                .Select(x => new Item(x.v.Product, x.v.Name, x.v.Sku, x.d.After, x.v.ReorderLevel))
                .OrderBy(i => i.Available)
                .ToList();
            if (worse.Count == 0) continue;

            var warehouse = await db.Warehouses.Where(w => w.Id == group.Key).Select(w => w.Name).SingleAsync(ct);
            await publisher.ToAudienceAsync(
                NotificationType.LowStock, group.Key, Describe(worse, warehouse) with { EntityType = "LowStock", EntityId = group.Key },
                includeActor: true, ct); // the person who took the last units should know too
        }
    }

    /// <summary>Same buckets as the stock list: 0 in stock, 1 low (≤ reorder level), 2 out (≤ 0 available).</summary>
    private static int Rank(decimal available, decimal reorderLevel) => available <= 0 ? 2 : available <= reorderLevel ? 1 : 0;

    private sealed record Item(string Product, string? Variant, string Sku, decimal Available, decimal ReorderLevel)
    {
        public bool Out => Available <= 0;
        public string Name => Variant is null ? Product : $"{Product} ({Variant})";
    }

    private static NotificationContent Describe(IReadOnlyList<Item> items, string warehouse)
    {
        if (items.Count == 1)
        {
            var i = items[0];
            return i.Out
                ? new($"{i.Name} is out of stock", $"{i.Sku}: {(i.Available < 0 ? $"{i.Available:0.###}" : "none")} available in {warehouse}.")
                : new($"{i.Name} is running low", $"{i.Sku}: {i.Available:0.###} available in {warehouse}, at or below the reorder level of {i.ReorderLevel:0.###}.");
        }

        var outCount = items.Count(i => i.Out);
        var title = outCount == items.Count ? $"{items.Count} items ran out in {warehouse}"
            : outCount == 0 ? $"{items.Count} items are running low in {warehouse}"
            : $"{items.Count} items need reordering in {warehouse}";

        static string List(IEnumerable<Item> list)
        {
            var all = list.ToList();
            var shown = string.Join(", ", all.Take(ListedItems).Select(i => i.Sku));
            return all.Count > ListedItems ? $"{shown} and {all.Count - ListedItems} more" : shown;
        }

        var parts = new List<string>();
        if (outCount > 0) parts.Add($"Out of stock: {List(items.Where(i => i.Out))}.");
        if (outCount < items.Count) parts.Add($"Running low: {List(items.Where(i => !i.Out))}.");
        return new(title, string.Join(" ", parts));
    }
}
