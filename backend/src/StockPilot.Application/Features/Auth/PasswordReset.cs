using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Auditing;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Common.Validation;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth;

public sealed record ForgotPasswordCommand(string Email);

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(x => x.Email).ValidEmail();
}

/// <summary>Always returns success, whether or not the email exists (prevents account enumeration).</summary>
public sealed class ForgotPasswordHandler(
    IAppDbContext db,
    IValidator<ForgotPasswordCommand> validator,
    ISecureTokenFactory tokenFactory,
    AuthEmailService emails,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task Handle(ForgotPasswordCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var email = User.NormalizeEmail(command.Email);
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.IsActive)
        {
            return;
        }

        var (raw, hash) = tokenFactory.Create();
        db.UserTokens.Add(UserToken.Create(
            user.Id, UserTokenPurpose.PasswordReset, hash, clock.GetUtcNow(), options.Value.PasswordResetTokenLifetime));
        await db.SaveChangesAsync(ct);

        // Not awaited: talking to the mail server takes hundreds of milliseconds, so waiting for it would make the
        // response for a REAL account measurably slower than for an unknown one, and that timing would reveal which
        // emails are registered. The send logs its own failures; the user can always ask again.
        _ = emails.SendPasswordResetAsync(user, raw, CancellationToken.None);
    }
}

public sealed record ResetPasswordCommand(string Token, string NewPassword);

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

/// <summary>
/// Sets a new password, then kills every existing session: if the reset was needed because the
/// account was compromised, the attacker's refresh and access tokens must stop working too.
/// </summary>
public sealed class ResetPasswordHandler(
    IAppDbContext db,
    IValidator<ResetPasswordCommand> validator,
    ISecureTokenFactory tokenFactory,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var now = clock.GetUtcNow();
        var hash = tokenFactory.Hash(command.Token);
        var token = await db.UserTokens
            .IgnoreQueryFilters()
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == UserTokenPurpose.PasswordReset, ct)
            ?? throw new DomainException("TOKEN_INVALID", "This link is invalid or has expired.");

        token.Consume(now);

        var user = token.User;
        user.SetPasswordHash(passwordHasher.Hash(command.NewPassword)); // also rotates the security stamp
        user.ConfirmEmail(); // they proved they own the inbox
        user.RecordSuccessfulLogin(now); // clears lockout

        var sessions = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(ct);
        sessions.ForEach(t => t.Revoke(now, RefreshTokenRevokeReason.PasswordChanged));

        db.Record(currentUser, clock, AuditEvents.PasswordReset, user.TenantId, user.Id);
        await db.SaveChangesAsync(ct);
    }
}
