using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Application.Features.Platform;

/// <summary>
/// Platform administration (Super Admin). Assumption A11: the platform sees companies as ACCOUNTS (name, status, who
/// owns it, how big it is, when it was last used), never their business data: no products, prices, stock or orders.
/// Counts are the only thing taken from inside a company.
///
/// The Super Admin has no tenant, so every tenant query filter would match nothing. This folder is one of the few the
/// architecture test allows to bypass them (IgnoreQueryFilters), and the endpoints are guarded by platform.* permissions
/// that no company role has.
/// </summary>
public sealed record TenantSummaryDto(
    Guid Id, string Name, string Slug, TenantStatus Status, string Currency, string TimeZone, DateTimeOffset CreatedAt,
    string? OwnerName, string? OwnerEmail, int ActiveUsers, int Warehouses, int Products, DateTimeOffset? LastActivityAt);

public sealed record ListTenantsQuery : PageRequest
{
    public TenantStatus? Status { get; init; }
}

public sealed record PlatformMetricsDto(
    int Companies, int ActiveCompanies, int SuspendedCompanies, int NewCompaniesLast30Days, int Users, int UsersActiveLast7Days);

public sealed record SuspendTenantCommand(string Reason);

public sealed class SuspendTenantValidator : AbstractValidator<SuspendTenantCommand>
{
    public SuspendTenantValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("Say why, for the record.").MaximumLength(500);
}

public static class PlatformEvents
{
    public const string TenantSuspended = "Platform.TenantSuspended";
    public const string TenantActivated = "Platform.TenantActivated";
}

public sealed class ListTenantsHandler(IAppDbContext db)
{
    public async Task<PagedResult<TenantSummaryDto>> Handle(ListTenantsQuery query, CancellationToken ct)
    {
        var tenants = db.Tenants.AsNoTracking();
        if (query.Status is { } status) tenants = tenants.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            // Find a company by its name, its ID, or its owner's email (what support usually gets asked with).
            tenants = tenants.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term)
                || db.Users.IgnoreQueryFilters().Any(u => u.TenantId == t.Id && u.Email.Contains(term)));
        }

        // Sort on the entity (EF can't order by a record it has to construct first).
        tenants = query.ParseSort() switch
        {
            ("name", false) => tenants.OrderBy(t => t.Name),
            ("name", true) => tenants.OrderByDescending(t => t.Name),
            ("createdat", false) => tenants.OrderBy(t => t.CreatedAt),
            ("lastactivityat", true) => tenants.OrderByDescending(t => db.Users.IgnoreQueryFilters().Where(u => u.TenantId == t.Id).Max(u => u.LastLoginAt)),
            ("lastactivityat", false) => tenants.OrderBy(t => db.Users.IgnoreQueryFilters().Where(u => u.TenantId == t.Id).Max(u => u.LastLoginAt)),
            ("activeusers", true) => tenants.OrderByDescending(t => db.Users.IgnoreQueryFilters().Count(u => u.TenantId == t.Id && u.IsActive && u.PasswordHash != null)),
            _ => tenants.OrderByDescending(t => t.CreatedAt),
        };

        return await tenants.Select(Project(db)).ToPagedResultAsync(query, ct);
    }

    internal static System.Linq.Expressions.Expression<Func<Tenant, TenantSummaryDto>> Project(IAppDbContext db) => t => new TenantSummaryDto(
        t.Id, t.Name, t.Slug, t.Status, t.Currency, t.TimeZone, t.CreatedAt,
        // The owner is the earliest Owner-role account: the person who registered the company.
        db.Users.IgnoreQueryFilters().Where(u => u.TenantId == t.Id && u.RoleId == SystemRoles.OwnerId)
            .OrderBy(u => u.CreatedAt).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
        db.Users.IgnoreQueryFilters().Where(u => u.TenantId == t.Id && u.RoleId == SystemRoles.OwnerId)
            .OrderBy(u => u.CreatedAt).Select(u => u.Email).FirstOrDefault(),
        db.Users.IgnoreQueryFilters().Count(u => u.TenantId == t.Id && u.IsActive && u.PasswordHash != null),
        db.Warehouses.IgnoreQueryFilters().Count(w => w.TenantId == t.Id && w.IsActive),
        db.Products.IgnoreQueryFilters().Count(p => p.TenantId == t.Id),
        db.Users.IgnoreQueryFilters().Where(u => u.TenantId == t.Id).Max(u => u.LastLoginAt));
}

public sealed class GetTenantHandler(IAppDbContext db)
{
    public async Task<TenantSummaryDto> Handle(Guid id, CancellationToken ct) =>
        await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(ListTenantsHandler.Project(db)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Company", id);
}

public sealed class GetPlatformMetricsHandler(IAppDbContext db, TimeProvider clock)
{
    public async Task<PlatformMetricsDto> Handle(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthAgo = now.AddDays(-30);
        var weekAgo = now.AddDays(-7);
        var tenants = await db.Tenants.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(t => t.Status == TenantStatus.Active),
                New = g.Count(t => t.CreatedAt >= monthAgo),
            })
            .SingleOrDefaultAsync(ct);
        var users = db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.TenantId != null && u.IsActive && u.PasswordHash != null);

        return new PlatformMetricsDto(
            tenants?.Total ?? 0, tenants?.Active ?? 0, (tenants?.Total ?? 0) - (tenants?.Active ?? 0), tenants?.New ?? 0,
            await users.CountAsync(ct),
            await users.CountAsync(u => u.LastLoginAt >= weekAgo, ct));
    }
}

/// <summary>
/// Suspending blocks the whole company at once: sign-in returns TENANT_SUSPENDED, and existing access and refresh
/// tokens fail session validation (which checks the company's status). Nothing is deleted, so reactivating restores
/// everything exactly as it was. The reason goes to the company's own audit log.
/// </summary>
public sealed class SetTenantStatusHandler(
    IAppDbContext db, IValidator<SuspendTenantCommand> validator, ICurrentUser currentUser, TimeProvider clock, GetTenantHandler get)
{
    public async Task<TenantSummaryDto> Handle(Guid id, SuspendTenantCommand? suspend, CancellationToken ct)
    {
        if (suspend is not null) await validator.ValidateAndThrowAsync(suspend, ct);
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Company", id);

        if (suspend is not null)
        {
            if (tenant.Status == TenantStatus.Suspended) throw new ConflictException("ALREADY_SUSPENDED", "This company is already suspended.");
            tenant.Suspend();
        }
        else
        {
            if (tenant.Status == TenantStatus.Active) throw new ConflictException("ALREADY_ACTIVE", "This company is already active.");
            tenant.Activate();
        }

        db.AuditLogs.Add(new AuditLog(
            tenant.Id, currentUser.UserId, suspend is null ? PlatformEvents.TenantActivated : PlatformEvents.TenantSuspended,
            nameof(Tenant), tenant.Id.ToString(),
            suspend is null ? null : JsonSerializer.Serialize(new { Reason = new { @new = suspend.Reason.Trim() } }),
            currentUser.IpAddress, currentUser.CorrelationId, clock.GetUtcNow()));

        await db.SaveChangesAsync(ct);
        return await get.Handle(id, ct);
    }
}
