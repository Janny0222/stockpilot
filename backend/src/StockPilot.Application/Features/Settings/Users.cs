using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Common.Validation;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Settings;

public enum UserStatus
{
    Active,
    Invited,
    Deactivated,
}

public sealed record UserWarehouseDto(Guid Id, string Code, string Name);

/// <summary>IsYou marks the signed-in user: the UI hides "deactivate" and the role picker for yourself.</summary>
public sealed record UserDto(
    Guid Id, string Email, string FirstName, string LastName, Guid RoleId, string RoleName, bool RestrictToAssignedWarehouses,
    UserStatus Status, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, IReadOnlyList<UserWarehouseDto> Warehouses, bool IsYou);

public sealed record ListUsersQuery : PageRequest
{
    public UserStatus? Status { get; init; }
    public Guid? RoleId { get; init; }
}

public sealed record InviteUserCommand(string Email, string FirstName, string LastName, Guid RoleId, IReadOnlyList<Guid>? WarehouseIds);

public sealed record UpdateUserCommand(string FirstName, string LastName, Guid RoleId, IReadOnlyList<Guid>? WarehouseIds);

public sealed class InviteUserValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("Choose a role.");
        RuleFor(x => x.WarehouseIds).Must(w => w is null || w.Count <= 100).WithMessage("Too many warehouses.");
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("Choose a role.");
        RuleFor(x => x.WarehouseIds).Must(w => w is null || w.Count <= 100).WithMessage("Too many warehouses.");
    }
}

/// <summary>Shared rules for "which role and which warehouses may this user get".</summary>
public sealed class UserAssignment(IAppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// Built-in tenant roles plus this company's own roles. Never Super Admin: platform access can't be granted from
    /// inside a company. Roles has no tenant query filter (system roles have no tenant), so the filter is written here.
    /// </summary>
    public IQueryable<Role> AssignableRoles()
    {
        var tenantId = currentUser.TenantId;
        return db.Roles.Where(r => (r.TenantId == null && r.Id != SystemRoles.SuperAdminId) || (tenantId != null && r.TenantId == tenantId));
    }

    public async Task<(Role Role, IReadOnlyList<Guid> WarehouseIds)> ResolveAsync(Guid roleId, IReadOnlyList<Guid>? warehouseIds, CancellationToken ct)
    {
        var role = await AssignableRoles().SingleOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw new ValidationException([new ValidationFailure("roleId", "Choose one of the company's roles.")]);

        // Warehouse scope only means something for warehouse-restricted roles; everyone else sees every warehouse.
        if (!role.RestrictToAssignedWarehouses)
        {
            return (role, []);
        }

        var ids = (warehouseIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new ValidationException([new ValidationFailure("warehouseIds", $"Pick at least one warehouse: {role.Name} only sees assigned warehouses.")]);
        }

        // Tenant query filter: another company's warehouse ID simply doesn't count.
        var found = await db.Warehouses.CountAsync(w => ids.Contains(w.Id), ct);
        if (found != ids.Count)
        {
            throw new ValidationException([new ValidationFailure("warehouseIds", "One or more warehouses don't exist.")]);
        }

        return (role, ids);
    }

    /// <summary>Replaces the user's warehouse access rows with exactly <paramref name="warehouseIds"/>.</summary>
    public async Task ReplaceWarehousesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> warehouseIds, CancellationToken ct)
    {
        var existing = await db.UserWarehouseAccess.Where(a => a.UserId == userId).ToListAsync(ct);
        db.UserWarehouseAccess.RemoveRange(existing.Where(a => !warehouseIds.Contains(a.WarehouseId)));
        db.UserWarehouseAccess.AddRange(warehouseIds
            .Where(id => existing.All(a => a.WarehouseId != id))
            .Select(id => new UserWarehouseAccess(tenantId, userId, id)));
    }
}

public sealed class ListUsersHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<PagedResult<UserDto>> Handle(ListUsersQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();

        if (query.RoleId is { } roleId) users = users.Where(u => u.RoleId == roleId);
        users = query.Status switch
        {
            UserStatus.Active => users.Where(u => u.IsActive && u.PasswordHash != null),
            UserStatus.Invited => users.Where(u => u.IsActive && u.PasswordHash == null),
            UserStatus.Deactivated => users.Where(u => !u.IsActive),
            _ => users,
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            users = users.Where(u => u.Email.Contains(term) || u.FirstName.ToLower().Contains(term) || u.LastName.ToLower().Contains(term));
        }

        users = query.ParseSort() switch
        {
            ("email", false) => users.OrderBy(u => u.Email),
            ("email", true) => users.OrderByDescending(u => u.Email),
            ("name", true) => users.OrderByDescending(u => u.FirstName).ThenByDescending(u => u.LastName),
            ("lastloginat", false) => users.OrderBy(u => u.LastLoginAt),
            ("lastloginat", true) => users.OrderByDescending(u => u.LastLoginAt),
            ("createdat", false) => users.OrderBy(u => u.CreatedAt),
            ("createdat", true) => users.OrderByDescending(u => u.CreatedAt),
            _ => users.OrderBy(u => u.FirstName).ThenBy(u => u.LastName),
        };

        return await users.Select(Project(db, currentUser.UserId)).ToPagedResultAsync(query, ct);
    }

    internal static System.Linq.Expressions.Expression<Func<User, UserDto>> Project(IAppDbContext db, Guid? currentUserId) => u =>
        new UserDto(
            u.Id, u.Email, u.FirstName, u.LastName, u.RoleId, u.Role.Name, u.Role.RestrictToAssignedWarehouses,
            !u.IsActive ? UserStatus.Deactivated : u.PasswordHash == null ? UserStatus.Invited : UserStatus.Active,
            u.LastLoginAt, u.CreatedAt,
            (from a in db.UserWarehouseAccess
             join w in db.Warehouses on a.WarehouseId equals w.Id
             where a.UserId == u.Id
             orderby w.Name
             select new UserWarehouseDto(w.Id, w.Code, w.Name)).ToList(),
            u.Id == currentUserId);
}

public sealed class GetUserHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<UserDto> Handle(Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(ListUsersHandler.Project(db, currentUser.UserId)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("User", id);
}

public sealed record RoleOptionDto(Guid Id, string Name, bool RestrictToAssignedWarehouses, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record PermissionDto(string Code, string Group, string Description);

public sealed record RolesOverviewDto(IReadOnlyList<RoleOptionDto> Roles, IReadOnlyList<PermissionDto> Permissions);

/// <summary>Roles you can assign, with what each one may do (read-only; custom roles are Phase 5).</summary>
public sealed class ListRolesHandler(IAppDbContext db, UserAssignment assignment)
{
    private static readonly Guid[] Order = [SystemRoles.OwnerId, SystemRoles.ManagerId, SystemRoles.InventoryStaffId, SystemRoles.SalesStaffId];

    public async Task<RolesOverviewDto> Handle(CancellationToken ct)
    {
        var roles = await assignment.AssignableRoles().AsNoTracking()
            .Select(r => new { r.Id, r.Name, r.RestrictToAssignedWarehouses, r.IsSystem, Permissions = r.Permissions.Select(p => p.PermissionCode).ToList() })
            .ToListAsync(ct);
        var permissions = await db.Permissions.AsNoTracking()
            .Where(p => p.Group != "Platform")
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description))
            .ToListAsync(ct);
        var catalogue = Domain.Identity.Permissions.All.Select(p => p.Code).ToList();

        return new RolesOverviewDto(
            roles.OrderBy(r => Array.IndexOf(Order, r.Id) is var i && i >= 0 ? i : int.MaxValue).ThenBy(r => r.Name)
                .Select(r => new RoleOptionDto(r.Id, r.Name, r.RestrictToAssignedWarehouses, r.IsSystem, r.Permissions.Order(StringComparer.Ordinal).ToList()))
                .ToList(),
            permissions.OrderBy(p => catalogue.IndexOf(p.Code)).ToList());
    }
}

/// <summary>
/// Creates the account WITHOUT a password and emails a single-use link. The invitee chooses their own password, so
/// nobody (not even the Owner) ever knows it, and the link proves they own the inbox.
/// </summary>
public sealed class InviteUserHandler(
    IAppDbContext db,
    IValidator<InviteUserCommand> validator,
    ICurrentUser currentUser,
    UserAssignment assignment,
    ISecureTokenFactory tokenFactory,
    AuthEmailService emails,
    TimeProvider clock,
    IOptions<AuthOptions> options,
    GetUserHandler get)
{
    public async Task<UserDto> Handle(InviteUserCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var tenantId = currentUser.TenantId ?? throw new ForbiddenException("NO_TENANT", "This account has no company.");
        var (role, warehouseIds) = await assignment.ResolveAsync(command.RoleId, command.WarehouseIds, ct);

        var email = User.NormalizeEmail(command.Email);
        if (await EmailAvailability.IsTakenAsync(db, email, ct))
        {
            throw new ConflictException("EMAIL_TAKEN", "An account with this email already exists.");
        }

        var user = User.CreateTenantUser(tenantId, role.Id, email, command.FirstName, command.LastName);
        db.Users.Add(user);
        db.UserWarehouseAccess.AddRange(warehouseIds.Select(w => new UserWarehouseAccess(tenantId, user.Id, w)));

        var (raw, hash) = tokenFactory.Create();
        db.UserTokens.Add(UserToken.Create(user.Id, UserTokenPurpose.Invitation, hash, clock.GetUtcNow(), options.Value.InvitationTokenLifetime));
        await db.SaveChangesAsync(ct); // a racing invite of the same email → 409 EMAIL_TAKEN (unique index)

        await SendAsync(db, currentUser, emails, user, raw, ct);
        return await get.Handle(user.Id, ct);
    }

    internal static async Task SendAsync(IAppDbContext db, ICurrentUser currentUser, AuthEmailService emails, User user, string raw, CancellationToken ct)
    {
        var inviter = await db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.FirstName + " " + u.LastName).SingleOrDefaultAsync(ct);
        var company = await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.Name).SingleAsync(ct);
        await emails.SendInvitationAsync(user, inviter?.Trim() is { Length: > 0 } name ? name : "Your team", company, raw, ct);
    }
}

/// <summary>New link for a pending invitation (lost email, or it expired). Earlier links stop working.</summary>
public sealed class ResendInvitationHandler(
    IAppDbContext db, ICurrentUser currentUser, ISecureTokenFactory tokenFactory, AuthEmailService emails, TimeProvider clock, IOptions<AuthOptions> options)
{
    public async Task Handle(Guid id, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        if (!user.IsInvitationPending) throw new ConflictException("INVITATION_ACCEPTED", "This person has already accepted the invitation.");
        if (!user.IsActive) throw new ConflictException("USER_DEACTIVATED", "Reactivate this user before sending a new invitation.");

        var now = clock.GetUtcNow();
        var old = await db.UserTokens.Where(t => t.UserId == id && t.Purpose == UserTokenPurpose.Invitation && t.ConsumedAt == null).ToListAsync(ct);
        old.Where(t => t.IsUsable(now)).ToList().ForEach(t => t.Consume(now));

        var (raw, hash) = tokenFactory.Create();
        db.UserTokens.Add(UserToken.Create(user.Id, UserTokenPurpose.Invitation, hash, now, options.Value.InvitationTokenLifetime));
        await db.SaveChangesAsync(ct);

        await InviteUserHandler.SendAsync(db, currentUser, emails, user, raw, ct);
    }
}

/// <summary>
/// Name, role and warehouse scope. A role change rotates the security stamp, so the user's current access token stops
/// working within the session-validation window and the next refresh carries the new role. Warehouse scope is read
/// live on every request, so it applies immediately.
/// </summary>
public sealed class UpdateUserHandler(
    IAppDbContext db, IValidator<UpdateUserCommand> validator, ICurrentUser currentUser, UserAssignment assignment, GetUserHandler get)
{
    public async Task<UserDto> Handle(Guid id, UpdateUserCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);

        // Changing your own role could lock the company out of its own settings (the last Owner demoting themselves).
        if (user.Id == currentUser.UserId && command.RoleId != user.RoleId)
        {
            throw new ConflictException("CANNOT_CHANGE_OWN_ROLE", "You can't change your own role. Ask another Owner to do it.");
        }

        var (role, warehouseIds) = await assignment.ResolveAsync(command.RoleId, command.WarehouseIds, ct);

        user.Rename(command.FirstName, command.LastName);
        if (role.Id != user.RoleId) user.ChangeRole(role.Id);
        await assignment.ReplaceWarehousesAsync(user.TenantId!.Value, user.Id, warehouseIds, ct);

        await db.SaveChangesAsync(ct);
        return await get.Handle(id, ct);
    }
}

/// <summary>
/// Deactivation signs the person out everywhere: the security stamp rotates (access tokens fail validation) and every
/// refresh token is revoked. Nothing is deleted: their name stays on the documents and ledger entries they created.
/// </summary>
public sealed class SetUserStatusHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task Handle(Guid id, bool active, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);

        if (active)
        {
            user.Activate();
        }
        else
        {
            if (user.Id == currentUser.UserId)
            {
                throw new ConflictException("CANNOT_DEACTIVATE_SELF", "You can't deactivate your own account.");
            }

            user.Deactivate();
            var now = clock.GetUtcNow();
            var sessions = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
            sessions.ForEach(t => t.Revoke(now, RefreshTokenRevokeReason.UserDeactivated));
        }

        await db.SaveChangesAsync(ct);
    }
}
