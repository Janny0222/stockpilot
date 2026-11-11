using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Auditing;
using StockPilot.Application.Common.Validation;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Common;
using StockPilot.Application.Features.Notifications;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Notifications;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Application.Features.Auth;

/// <summary>What the "accept invitation" page shows before the invitee picks a password.</summary>
public sealed record InvitationDto(string Email, string FirstName, string LastName, string CompanyName, string RoleName);

public sealed record AcceptInvitationCommand(string Token, string Password);

public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).StrongPassword();
    }
}

internal static class InvitationLookup
{
    /// <summary>
    /// One message for every failure (unknown, used, expired, user deactivated, company suspended), so a token can't be
    /// used to learn anything about an account.
    /// </summary>
    public static DomainException Invalid() => new("TOKEN_INVALID", "This invitation is invalid or has expired. Ask for a new one.");

    public static async Task<UserToken> FindUsableAsync(IAppDbContext db, ISecureTokenFactory tokens, string? rawToken, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw Invalid();

        // Anonymous request: no tenant yet, so look past the filters. The token hash is the only key.
        var hash = tokens.Hash(rawToken);
        var token = await db.UserTokens.IgnoreQueryFilters()
            .Include(t => t.User).ThenInclude(u => u.Role)
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == UserTokenPurpose.Invitation, ct);

        if (token is null || !token.IsUsable(now) || !token.User.IsActive || !token.User.IsInvitationPending) throw Invalid();

        var tenantActive = await db.Tenants.AnyAsync(t => t.Id == token.User.TenantId && t.Status == TenantStatus.Active, ct);
        return tenantActive ? token : throw Invalid();
    }
}

/// <summary>Read-only peek (the token travels in a POST body, never in a URL, so it stays out of access logs).</summary>
public sealed class GetInvitationHandler(IAppDbContext db, ISecureTokenFactory tokens, TimeProvider clock)
{
    public async Task<InvitationDto> Handle(string? rawToken, CancellationToken ct)
    {
        var token = await InvitationLookup.FindUsableAsync(db, tokens, rawToken, clock.GetUtcNow(), ct);
        var user = token.User;
        var company = await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.Name).SingleAsync(ct);
        return new InvitationDto(user.Email, user.FirstName, user.LastName, company, user.Role.Name);
    }
}

/// <summary>
/// Sets the first password, confirms the email (the link proved the inbox) and signs the person straight in.
/// </summary>
public sealed class AcceptInvitationHandler(
    IAppDbContext db,
    IValidator<AcceptInvitationCommand> validator,
    ISecureTokenFactory tokens,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer,
    ICurrentUser currentUser,
    TimeProvider clock,
    NotificationPublisher notifications)
{
    public async Task<AuthResult> Handle(AcceptInvitationCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var now = clock.GetUtcNow();
        var token = await InvitationLookup.FindUsableAsync(db, tokens, command.Token, now, ct);
        var user = token.User;

        token.Consume(now);
        user.SetPasswordHash(passwordHasher.Hash(command.Password));
        user.ConfirmEmail();
        user.RecordSuccessfulLogin(now);

        var (result, _) = tokenIssuer.Issue(user, familyId: Guid.CreateVersion7(), currentUser.IpAddress, currentUser.UserAgent);
        db.Record(currentUser, clock, AuditEvents.InvitationAccepted, user.TenantId, user.Id);
        await NotifyTeamAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// Tells the people who manage users that the invitee is in. This request is anonymous (no tenant yet), so the
    /// tenant filters can't scope the query: it bypasses them and names the invitee's company explicitly instead.
    /// </summary>
    private async Task NotifyTeamAsync(User user, CancellationToken ct)
    {
        var tenantId = user.TenantId!.Value;
        const NotificationType type = NotificationType.UserJoined;
        var permission = NotificationCatalog.Get(type).Permission;

        var recipients = await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.Id != user.Id && u.IsActive && u.PasswordHash != null)
            .Where(u => db.RolePermissions.Any(rp => rp.RoleId == u.RoleId && rp.PermissionCode == permission))
            .Where(u => !db.NotificationPreferences.IgnoreQueryFilters()
                .Any(p => p.TenantId == tenantId && p.UserId == u.Id && p.Type == type && !p.InApp))
            .Select(u => u.Id)
            .ToListAsync(ct);

        notifications.Add(tenantId, type, recipients, new NotificationContent(
            $"{user.FullName} joined",
            $"{user.FirstName} accepted the invitation and signed in as {user.Role.Name}.",
            "User", user.Id));
    }
}
