using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Domain.Tenancy;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.Infrastructure.Identity;

/// <summary>Role → permission codes, cached for 5 minutes (roles change rarely; cache is evicted on edit in Phase 5).</summary>
internal sealed class PermissionService(AppDbContext db, IMemoryCache cache) : IPermissionService
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var permissions = await cache.GetOrCreateAsync($"role-permissions:{roleId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var codes = await db.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.PermissionCode)
                .ToListAsync(cancellationToken);
            return (IReadOnlySet<string>)codes.ToHashSet(StringComparer.Ordinal);
        });

        return permissions ?? new HashSet<string>();
    }
}

internal sealed class SessionValidator(AppDbContext db, IMemoryCache cache, IOptions<AuthOptions> options) : ISessionValidator
{
    public async Task<bool> IsValidAsync(Guid userId, string securityStamp, CancellationToken cancellationToken)
    {
        var cacheDuration = options.Value.SessionValidationCacheDuration;
        var key = $"session:{userId}:{securityStamp}";

        if (cacheDuration > TimeSpan.Zero && cache.TryGetValue(key, out bool cached))
        {
            return cached;
        }

        // The user ID comes from a signature-verified token, so bypassing the tenant filter is safe.
        var state = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.IsActive,
                u.SecurityStamp,
                TenantActive = u.TenantId == null
                    || db.Tenants.Any(t => t.Id == u.TenantId && t.Status == TenantStatus.Active),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var valid = state is not null && state.IsActive && state.TenantActive && state.SecurityStamp == securityStamp;

        if (cacheDuration > TimeSpan.Zero)
        {
            cache.Set(key, valid, cacheDuration);
        }

        return valid;
    }
}
