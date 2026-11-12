using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Exceptions;

namespace StockPilot.Infrastructure.Persistence;

public enum IdempotencyClaim
{
    /// <summary>First time we see this key: go ahead and execute.</summary>
    Claimed,

    /// <summary>Seen and finished: return the stored response.</summary>
    Completed,

    /// <summary>Seen and still running (a concurrent duplicate).</summary>
    InProgress,
}

public sealed record IdempotencyLookup(IdempotencyClaim Claim, int? StatusCode = null, string? ResponseBody = null);

/// <summary>Storage side of the Idempotency-Key protocol (the API filter drives it).</summary>
public sealed class IdempotencyStore(AppDbContext db, TimeProvider clock)
{
    public async Task<IdempotencyLookup> ClaimAsync(Guid tenantId, Guid? userId, string key, string requestHash, CancellationToken ct)
    {
        var existing = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Key == key, ct);

        if (existing is null)
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord(tenantId, key, userId, requestHash, clock.GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(ct);
                return new IdempotencyLookup(IdempotencyClaim.Claimed);
            }
            catch (ConflictException)
            {
                // Lost the race to an identical request that claimed the key a moment ago.
                db.ChangeTracker.Clear();
                return new IdempotencyLookup(IdempotencyClaim.InProgress);
            }
        }

        // A key belongs to the person who first used it. Someone else in the company replaying it (even with the same
        // body) must never receive the first person's stored response.
        if (existing.RequestHash != requestHash || existing.UserId != userId)
        {
            throw new Application.Common.Exceptions.ConflictException(
                "IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used for a different request.");
        }

        return existing.StatusCode is null
            ? new IdempotencyLookup(IdempotencyClaim.InProgress)
            : new IdempotencyLookup(IdempotencyClaim.Completed, existing.StatusCode, existing.ResponseBody);
    }

    public Task CompleteAsync(Guid tenantId, string key, int statusCode, string? responseBody, CancellationToken ct) =>
        db.IdempotencyRecords
            .Where(r => r.TenantId == tenantId && r.Key == key)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StatusCode, statusCode).SetProperty(r => r.ResponseBody, responseBody), ct);

    /// <summary>The request failed: release the key so the client may retry with it.</summary>
    public Task ReleaseAsync(Guid tenantId, string key, CancellationToken ct) =>
        db.IdempotencyRecords.Where(r => r.TenantId == tenantId && r.Key == key).ExecuteDeleteAsync(ct);
}
