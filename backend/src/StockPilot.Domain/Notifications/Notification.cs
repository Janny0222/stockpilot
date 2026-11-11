using StockPilot.Domain.Common;

namespace StockPilot.Domain.Notifications;

/// <summary>What happened. Stored as text, so add new values freely; never rename one.</summary>
public enum NotificationType
{
    /// <summary>An item's available stock fell to or below its reorder level, or ran out.</summary>
    LowStock = 1,

    /// <summary>A purchase order was submitted and needs someone to approve it.</summary>
    PurchaseOrderAwaitingApproval = 2,

    /// <summary>A purchase order was approved: goods are on their way to a warehouse.</summary>
    PurchaseOrderApproved = 3,

    /// <summary>A sales order was confirmed: its stock is reserved and it's ready to ship.</summary>
    SalesOrderReadyToShip = 4,

    /// <summary>An invited person accepted and can now sign in.</summary>
    UserJoined = 5,
}

/// <summary>
/// One message for one person. Written in the SAME transaction as the change it describes, so a rolled-back change
/// never leaves a notification behind and a committed one never misses its notification. Live push (SignalR, M11)
/// will only ever announce rows that already exist, so a missed push is recovered by the next list request.
/// </summary>
public sealed class Notification : Entity, ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int MessageMaxLength = 1000;

    private Notification() { }

    public Notification(
        Guid tenantId, Guid recipientUserId, NotificationType type, string title, string message,
        string? entityType, Guid? entityId, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        RecipientUserId = recipientUserId;
        Type = type;
        Title = Truncate(title, TitleMaxLength);
        Message = Truncate(message, MessageMaxLength);
        EntityType = entityType;
        EntityId = entityId;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    /// <summary>What to open when clicked, e.g. ("PurchaseOrder", id). The frontend maps it to a page.</summary>
    public string? EntityType { get; private set; }
    public Guid? EntityId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>
/// Opt-out per person and type. No row means "on": a new type reaches everyone it concerns until they turn it off.
/// Email delivery (M11) will add an Email flag next to InApp.
/// </summary>
public sealed class NotificationPreference : ITenantOwned
{
    private NotificationPreference() { }

    public NotificationPreference(Guid tenantId, Guid userId, NotificationType type, bool inApp)
    {
        TenantId = tenantId;
        UserId = userId;
        Type = type;
        InApp = inApp;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public bool InApp { get; private set; }

    public void Set(bool inApp) => InApp = inApp;
}
