using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Notifications;

namespace StockPilot.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Pushes new notifications live, but only once they are really committed. Use cases just add Notification rows; this
/// watches the unit of work:
///   SaveChanges outside a transaction → it committed on its own → push right away.
///   SaveChanges inside ExecuteInTransactionAsync → hold the rows → push on COMMIT, drop them on rollback.
/// So nobody is ever told about stock that "ran out" in a transaction that then failed (INSUFFICIENT_STOCK, a version
/// conflict, a deadlock retry). A push that fails is only logged: the database already has the notification and the
/// bell reads it from there.
/// </summary>
internal sealed class NotificationDispatchInterceptor(IRealtimeNotifier notifier, ILogger<NotificationDispatchInterceptor> logger)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private List<RealtimeNotification> _saving = [];
    private readonly List<RealtimeNotification> _awaitingCommit = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await SavedAsync(eventData.Context);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _saving = [];

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving = [];
        return Task.CompletedTask;
    }

    // A new attempt (ExecuteInTransactionAsync retries the whole unit of work) starts clean: rows staged by a failed
    // attempt never existed.
    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        _awaitingCommit.Clear();
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return ValueTask.FromResult(result);
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        DispatchAsync(TakeAwaiting()).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        DispatchAsync(TakeAwaiting());

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _awaitingCommit.Clear();

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _awaitingCommit.Clear();
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;
        context.ChangeTracker.DetectChanges();
        _saving = context.ChangeTracker.Entries<Notification>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Select(n => new RealtimeNotification(n.RecipientUserId, n.Id, n.Type, n.Title, n.Message, n.EntityType, n.EntityId, n.CreatedAt))
            .ToList();
    }

    private Task SavedAsync(DbContext? context)
    {
        var saved = _saving;
        _saving = [];
        if (saved.Count == 0) return Task.CompletedTask;

        if (context?.Database.CurrentTransaction is not null)
        {
            _awaitingCommit.AddRange(saved);
            return Task.CompletedTask;
        }

        return DispatchAsync(saved);
    }

    private List<RealtimeNotification> TakeAwaiting()
    {
        var items = _awaitingCommit.ToList();
        _awaitingCommit.Clear();
        return items;
    }

    private async Task DispatchAsync(List<RealtimeNotification> items)
    {
        if (items.Count == 0) return;
        try
        {
            await notifier.PushAsync(items, CancellationToken.None); // the request may already be finishing
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live push of {Count} notifications failed; they are still in the database", items.Count);
        }
    }
}

/// <summary>Default when no live channel is wired (background jobs, tools): notifications are simply read later.</summary>
internal sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task PushAsync(IReadOnlyList<RealtimeNotification> notifications, CancellationToken cancellationToken) => Task.CompletedTask;
}
