using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Catalog;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.Infrastructure.Storage;

/// <summary>
/// Stores file bytes in PostgreSQL (table stored_files).
///
/// Why not disk or an object store? Render's free tier has an ephemeral filesystem (files vanish on
/// redeploy), and Cloudflare R2 needs a payment method on file. Product images are re-encoded to
/// small WebP files (typically 30–150 KB), so a few hundred of them fit easily in Neon's free storage.
/// Swapping to S3/R2 later means writing one new IFileStorage implementation; nothing else changes.
///
/// Reads go through the tenant query filter, so one company can never fetch another's file by key.
/// </summary>
internal sealed class DatabaseFileStorage(AppDbContext db, TimeProvider clock) : IFileStorage
{
    public Task SaveAsync(Guid tenantId, string key, string contentType, byte[] data, CancellationToken cancellationToken)
    {
        // Staged only: commits in the caller's SaveChanges, atomically with the image metadata row.
        db.StoredFiles.Add(new StoredFile(tenantId, key, contentType, data, clock.GetUtcNow()));
        return Task.CompletedTask;
    }

    public async Task<StoredFileContent?> ReadAsync(string key, CancellationToken cancellationToken) =>
        await db.StoredFiles
            .AsNoTracking()
            .Where(f => f.Key == key)
            .Select(f => new StoredFileContent(f.Data, f.ContentType))
            .SingleOrDefaultAsync(cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        db.StoredFiles.Where(f => f.Key == key).ExecuteDeleteAsync(cancellationToken);
}
