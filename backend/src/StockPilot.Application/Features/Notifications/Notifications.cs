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

/// <summary>EmailLabel is null when the type has no email version (the UI then shows no email switch).</summary>
public sealed record NotificationPreferenceDto(NotificationType Type, string Label, string Description, bool InApp, bool Email, string? EmailLabel);

/// <summary>Null leaves that switch as it is, so the UI can flip one switch without knowing the others.</summary>
public sealed record NotificationPreferenceInput(NotificationType Type, bool? InApp, bool? Email);

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
        var saved = await db.NotificationPreferences.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Type, ct);

        return NotificationCatalog.All
            .Where(t => granted.Contains(t.Permission))
            .Select(t =>
            {
                saved.TryGetValue(t.Type, out var p);
                return new NotificationPreferenceDto(
                    t.Type, t.Label, t.Description,
                    p?.InApp ?? NotificationPreference.DefaultInApp,
                    t.EmailLabel is not null && (p?.Email ?? NotificationPreference.DefaultEmail),
                    t.EmailLabel);
            })
            .ToList();
    }
}

public sealed class UpdateNotificationPreferencesValidator : AbstractValidator<UpdateNotificationPreferencesCommand>
{
    public UpdateNotificationPreferencesValidator()
    {
        RuleFor(x => x.Preferences).NotNull();
        RuleForEach(x => x.Preferences).Must(p => Enum.IsDefined(p.Type)).WithMessage("Unknown notification type.");
        RuleForEach(x => x.Preferences)
            .Must(p => p.Email != true || NotificationCatalog.All.Any(t => t.Type == p.Type && t.EmailLabel is not null))
            .WithMessage("This notification isn't available by email.");
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
            // Partial update: a switch the request doesn't mention keeps its current (or default) value.
            if (existing.TryGetValue(input.Type, out var row)) row.Set(input.InApp ?? row.InApp, input.Email ?? row.Email);
            else db.NotificationPreferences.Add(new NotificationPreference(
                tenantId, userId, input.Type, input.InApp ?? NotificationPreference.DefaultInApp, input.Email ?? NotificationPreference.DefaultEmail));
        }

        await db.SaveChangesAsync(ct);
        return await get.Handle(ct);
    }
}
