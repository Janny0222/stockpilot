using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Auditing;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth;

public sealed record LoginCommand(string Email, string Password);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class LoginHandler(
    IAppDbContext db,
    IValidator<LoginCommand> validator,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    // Used to spend the same CPU time when the email doesn't exist, so response timing
    // doesn't reveal which emails are registered (user enumeration).
    private static string? _dummyHash;

    public async Task<AuthResult> Handle(LoginCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var now = clock.GetUtcNow();
        var email = User.NormalizeEmail(command.Email);
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || user.PasswordHash is null)
        {
            _dummyHash ??= passwordHasher.Hash("timing-equaliser-not-a-real-password");
            passwordHasher.Verify(_dummyHash, command.Password);
            throw InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            throw new AuthenticationFailedException(
                "ACCOUNT_LOCKED", "Too many failed attempts. Try again later or reset your password.");
        }

        var check = passwordHasher.Verify(user.PasswordHash, command.Password);
        if (check == PasswordCheck.Failed)
        {
            user.RecordFailedLogin(now, options.Value.MaxFailedLoginAttempts, options.Value.LockoutDuration);
            db.Record(currentUser, clock, AuditEvents.LoginFailed, user.TenantId, user.Id);
            await db.SaveChangesAsync(ct);
            throw InvalidCredentials();
        }

        // Only reveal account state AFTER the password is proven, so these messages can't be used to probe accounts.
        if (!user.IsActive)
        {
            throw new ForbiddenException("ACCOUNT_DISABLED", "This account has been deactivated.");
        }

        if (user.TenantId is { } tenantId &&
            !await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == Domain.Tenancy.TenantStatus.Active, ct))
        {
            throw new ForbiddenException("TENANT_SUSPENDED", "This company account is suspended.");
        }

        if (options.Value.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            throw new ForbiddenException("EMAIL_NOT_VERIFIED", "Please verify your email address before signing in.");
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.UpgradePasswordHash(passwordHasher.Hash(command.Password));
        }

        user.RecordSuccessfulLogin(now);
        var (result, _) = tokenIssuer.Issue(user, familyId: Guid.CreateVersion7(), currentUser.IpAddress, currentUser.UserAgent);
        db.Record(currentUser, clock, AuditEvents.LoginSucceeded, user.TenantId, user.Id);
        await db.SaveChangesAsync(ct);

        return result;
    }

    private static AuthenticationFailedException InvalidCredentials() =>
        new("INVALID_CREDENTIALS", "Email or password is incorrect.");
}
