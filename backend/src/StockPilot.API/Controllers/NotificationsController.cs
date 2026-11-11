using Microsoft.AspNetCore.Mvc;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Notifications;

namespace StockPilot.API.Controllers;

/// <summary>
/// Your own notifications. No permission attribute: every signed-in user has an inbox (the fallback policy still
/// requires authentication), and every query is limited to the caller as recipient.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] ListNotificationsQuery query, [FromServices] ListNotificationsHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("unread-count")]
    public Task<UnreadCountDto> UnreadCount([FromServices] GetUnreadCountHandler handler, CancellationToken ct) => handler.Handle(ct);

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, [FromServices] MarkNotificationReadHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public Task<MarkAllReadResult> MarkAllRead([FromServices] MarkAllNotificationsReadHandler handler, CancellationToken ct) => handler.Handle(ct);

    [HttpGet("preferences")]
    public Task<IReadOnlyList<NotificationPreferenceDto>> Preferences([FromServices] GetNotificationPreferencesHandler handler, CancellationToken ct) =>
        handler.Handle(ct);

    [HttpPut("preferences")]
    public Task<IReadOnlyList<NotificationPreferenceDto>> UpdatePreferences(
        UpdateNotificationPreferencesCommand command, [FromServices] UpdateNotificationPreferencesHandler handler, CancellationToken ct) =>
        handler.Handle(command, ct);
}
