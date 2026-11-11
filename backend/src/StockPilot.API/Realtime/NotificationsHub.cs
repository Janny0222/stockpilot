using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Infrastructure.Identity;

namespace StockPilot.API.Realtime;

/// <summary>
/// One-way channel: the server tells a signed-in user "you have a new notification". Clients never call the hub, so
/// there's nothing to authorize per method; [Authorize] runs the same JWT + session validation as every API call.
///
/// Single Render instance → no backplane. With more instances, add one (Redis, or Azure SignalR) so a push from
/// instance A reaches a browser connected to instance B.
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/api/v1/hubs/notifications";
    public const string NotificationMethod = "notification";
}

/// <summary>Routes pushes by our own user-ID claim ("sub"), the same ID notifications are addressed to.</summary>
public sealed class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst(StockPilotClaims.UserId)?.Value;
}

/// <summary>The live channel behind IRealtimeNotifier. Sends to the recipient's connections only (every tab they have open).</summary>
public sealed class SignalRRealtimeNotifier(IHubContext<NotificationsHub> hub) : IRealtimeNotifier
{
    public Task PushAsync(IReadOnlyList<RealtimeNotification> notifications, CancellationToken cancellationToken) =>
        Task.WhenAll(notifications.Select(n => hub.Clients.User(n.RecipientUserId.ToString()).SendAsync(
            NotificationsHub.NotificationMethod,
            new { n.Id, n.Type, n.Title, n.Message, n.EntityType, n.EntityId, n.CreatedAt, ReadAt = (DateTimeOffset?)null },
            cancellationToken)));
}
