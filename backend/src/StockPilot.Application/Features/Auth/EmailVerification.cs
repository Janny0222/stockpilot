using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Common.Validation;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth;

public sealed record VerifyEmailCommand(string Token);

public sealed class VerifyEmailHandler(IAppDbContext db, ISecureTokenFactory tokenFactory, TimeProvider clock)
{
    public async Task Handle(VerifyEmailCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
        {
            throw new DomainException("TOKEN_INVALID", "This link is invalid or has expired.");
        }

        var hash = tokenFactory.Hash(command.Token);
        var token = await db.UserTokens
            .IgnoreQueryFilters()
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == UserTokenPurpose.EmailVerification, ct)
            ?? throw new DomainException("TOKEN_INVALID", "This link is invalid or has expired.");

        token.Consume(clock.GetUtcNow());
        token.User.ConfirmEmail();
        await db.SaveChangesAsync(ct);
    }
}

public sealed record ResendVerificationCommand(string Email);

public sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationCommand>
{
    public ResendVerificationValidator() => RuleFor(x => x.Email).ValidEmail();
}

/// <summary>Always "succeeds" from the caller's view, so it can't be used to discover which emails exist.</summary>
public sealed class ResendVerificationHandler(
    IAppDbContext db,
    IValidator<ResendVerificationCommand> validator,
    ISecureTokenFactory tokenFactory,
    AuthEmailService emails,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task Handle(ResendVerificationCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var email = User.NormalizeEmail(command.Email);
        var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || user.EmailConfirmed || !user.IsActive)
        {
            return;
        }

        var (raw, hash) = tokenFactory.Create();
        db.UserTokens.Add(UserToken.Create(
            user.Id, UserTokenPurpose.EmailVerification, hash, clock.GetUtcNow(),
            options.Value.EmailVerificationTokenLifetime));
        await db.SaveChangesAsync(ct);

        await emails.SendEmailVerificationAsync(user, raw, ct);
    }
}
