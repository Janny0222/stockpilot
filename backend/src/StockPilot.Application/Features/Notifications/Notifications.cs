using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Domain.Notifications;

namespace StockPilot.Application.Features.Notifications;

public sealed record NotificationDto(
    Guid Id, NotificationType Type, string Title, string Message, string? EntityType, Guid? EntityId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record ListNotificationsQuery : PageRequest
{
    public bool UnreadOnly { get; init; }
}

public sealed record UnreadCountDto(int Count);

public sealed record MarkAllReadResult(int Marked);

public sealed record NotificationPreferenceDto(NotificationType Type, string Label, string Description, bool InApp);

public sealed record NotificationPreferenceInput(NotificationType Type, bool InApp);

public sealed record UpdateNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceInput> Preferences);

/// <summary>Everything here is "my notifications": the tenant filter plus recipient = the signed-in user.</summary>
internal static class MyNotifications
{
    public static Guid UserId(ICurrentUser currentUser) =>
        currentUser.UserId ?? throw new AuthenticationFailedException("UNAUTHENTICATED", "Not signed in.");

    public static IQueryable<Notification> Of(IAppDbContext db, ICurrentUser currentUser)
    {
        var userId = UserId(currentUser);
        return db.Notifications.Where(n => n.RecipientUserId == userId);
    }
}

public sealed class ListNotificationsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public Task<PagedResult<NotificationDto>> Handle(ListNotificationsQuery query, CancellationToken ct)
    {
        var mine = MyNotifications.Of(db, currentUser).AsNoTracking();
        if (query.UnreadOnly) mine = mine.Where(n => n.ReadAt == null);

        return mine
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.EntityType, n.EntityId, n.CreatedAt, n.ReadAt))
            .ToPagedResultAsync(query, ct);
    }
}

/// <summary>Polled by the bell (every minute while the tab is visible), so it's one indexed COUNT.</summary>
public sealed class GetUnreadCountHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<UnreadCountDto> Handle(CancellationToken ct) =>
        new(await MyNotifications.Of(db, currentUser).CountAsync(n => n.ReadAt == null, ct));
}

public sealed class MarkNotificationReadHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task Handle(Guid id, CancellationToken ct)
    {
        // Someone else's notification (or another company's) is simply "not found".
        var notification = await MyNotifications.Of(db, currentUser).SingleOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException("Notification", id);
        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }
}

public sealed class MarkAllNotificationsReadHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<MarkAllReadResult> Handle(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // One UPDATE … WHERE recipient = me AND read_at IS NULL, instead of loading every row.
        var marked = await MyNotifications.Of(db, currentUser).Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return new MarkAllReadResult(marked);
    }
}

/// <summary>Only the types that concern you (by permission) are listed, so nobody sees switches that do nothing.</summary>
public sealed class GetNotificationPreferencesHandler(IAppDbContext db, ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<IReadOnlyList<NotificationPreferenceDto>> Handle(CancellationToken ct)
    {
        var userId = MyNotifications.UserId(currentUser);
        var granted = currentUser.RoleId is { } roleId ? await permissions.GetPermissionsAsync(roleId, ct) : new HashSet<string>();
        var off = await db.NotificationPreferences.Where(p => p.UserId == userId && !p.InApp).Select(p => p.Type).ToListAsync(ct);

        return NotificationCatalog.All
            .Where(t => granted.Contains(t.Permission))
            .Select(t => new NotificationPreferenceDto(t.Type, t.Label, t.Description, !off.Contains(t.Type)))
            .ToList();
    }
}

public sealed class UpdateNotificationPreferencesValidator : AbstractValidator<UpdateNotificationPreferencesCommand>
{
    public UpdateNotificationPreferencesValidator()
    {
        RuleFor(x => x.Preferences).NotNull();
        RuleForEach(x => x.Preferences).Must(p => Enum.IsDefined(p.Type)).WithMessage("Unknown notification type.");
    }
}

public sealed class UpdateNotificationPreferencesHandler(
    IAppDbContext db, IValidator<UpdateNotificationPreferencesCommand> validator, ICurrentUser currentUser, GetNotificationPreferencesHandler get)
{
    public async Task<IReadOnlyList<NotificationPreferenceDto>> Handle(UpdateNotificationPreferencesCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var userId = MyNotifications.UserId(currentUser);
        var tenantId = currentUser.TenantId ?? throw new ForbiddenException("NO_TENANT", "This account has no company.");

        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Type, ct);
        foreach (var input in command.Preferences.DistinctBy(p => p.Type))
        {
            if (existing.TryGetValue(input.Type, out var row)) row.Set(input.InApp);
            else db.NotificationPreferences.Add(new NotificationPreference(tenantId, userId, input.Type, input.InApp));
        }

        await db.SaveChangesAsync(ct);
        return await get.Handle(ct);
    }
}
