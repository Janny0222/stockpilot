using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Second line of defence for writes. Query filters stop you *reading* another tenant's rows; this stops
/// you *writing* them (e.g. an entity attached from a deserialized request, or a handler bug).
///
/// When the caller has a tenant, every inserted/updated/deleted tenant-owned row must belong to it and
/// TenantId may never change. Requests without a tenant (registration, login, platform admin) are
/// server-controlled flows, so they are not restricted here.
/// </summary>
internal sealed class TenantIsolationInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Guard(DbContext? context)
    {
        if (context is null || currentUser.TenantId is not { } tenantId)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();

        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            Guid? rowTenant = entry.Entity switch
            {
                ITenantOwned owned => owned.TenantId,
                User user => user.TenantId,
                Tenant tenant => tenant.Id,
                _ => tenantId, // not tenant-scoped (tokens, audit rows): nothing to check
            };

            if (rowTenant != tenantId)
            {
                throw new TenantIsolationException(
                    $"Blocked {entry.State} of {entry.Metadata.ClrType.Name} belonging to another tenant.");
            }

            if (entry.State == EntityState.Modified &&
                entry.Metadata.FindProperty(nameof(ITenantOwned.TenantId)) is not null &&
                entry.Property(nameof(ITenantOwned.TenantId)).IsModified)
            {
                throw new TenantIsolationException($"TenantId of {entry.Metadata.ClrType.Name} cannot be changed.");
            }
        }
    }
}
