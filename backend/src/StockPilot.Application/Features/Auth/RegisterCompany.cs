using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Options;
using StockPilot.Application.Common.Validation;
using StockPilot.Application.Features.Auth.Common;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Tenancy;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Application.Features.Auth;

public sealed record RegisterCompanyCommand(
    string CompanyName,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string Currency = "USD",
    string TimeZone = "UTC");

public sealed record RegisterCompanyResult(Guid TenantId, Guid UserId, bool EmailVerificationRequired);

public sealed class RegisterCompanyValidator : AbstractValidator<RegisterCompanyCommand>
{
    public RegisterCompanyValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MinimumLength(2).MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.FirstName).PersonName("First name");
        RuleFor(x => x.LastName).PersonName("Last name");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Currency).NotEmpty().Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO code.");
        // IANA zone shape check ("Asia/Manila", "UTC"). Full validation happens where zones are used (reports).
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(64).Matches(@"^[A-Za-z_]+(/[A-Za-z0-9_+\-]+)*$");
    }
}

/// <summary>
/// Creates a company, its Owner, and a default warehouse in ONE SaveChanges call. EF wraps a single
/// SaveChanges in a database transaction, so either all three rows exist or none do.
/// </summary>
public sealed class RegisterCompanyHandler(
    IAppDbContext db,
    IValidator<RegisterCompanyCommand> validator,
    IPasswordHasher passwordHasher,
    ISecureTokenFactory tokenFactory,
    AuthEmailService emails,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task<RegisterCompanyResult> Handle(RegisterCompanyCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var email = User.NormalizeEmail(command.Email);

        // Users are tenant-filtered. A registering visitor has no tenant, so look across all tenants.
        if (await EmailAvailability.IsTakenAsync(db, email, ct))
        {
            throw new ConflictException("EMAIL_TAKEN", "An account with this email already exists.");
        }

        var tenant = Tenant.Register(
            command.CompanyName, await GenerateUniqueSlugAsync(command.CompanyName, ct),
            command.Currency, command.TimeZone);

        var owner = User.CreateTenantUser(tenant.Id, SystemRoles.OwnerId, email, command.FirstName, command.LastName);
        owner.SetPasswordHash(passwordHasher.Hash(command.Password));

        var warehouse = new Warehouse(tenant.Id, "MAIN", "Main Warehouse", isDefault: true);

        db.Tenants.Add(tenant);
        db.Users.Add(owner);
        db.Warehouses.Add(warehouse);

        var requireVerification = options.Value.RequireConfirmedEmail;
        string? rawToken = null;
        if (requireVerification)
        {
            (rawToken, var hash) = tokenFactory.Create();
            db.UserTokens.Add(UserToken.Create(
                owner.Id, UserTokenPurpose.EmailVerification, hash, clock.GetUtcNow(),
                options.Value.EmailVerificationTokenLifetime));
        }
        else
        {
            owner.ConfirmEmail();
        }

        // A race with a parallel registration of the same email is caught by the unique index and
        // surfaces as a ConflictException (translated in AppDbContext.SaveChangesAsync).
        await db.SaveChangesAsync(ct);

        if (rawToken is not null)
        {
            await emails.SendEmailVerificationAsync(owner, rawToken, ct);
        }

        return new RegisterCompanyResult(tenant.Id, owner.Id, requireVerification);
    }

    private async Task<string> GenerateUniqueSlugAsync(string companyName, CancellationToken ct)
    {
        var baseSlug = Slug.From(companyName);
        var candidate = baseSlug;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!await db.Tenants.AnyAsync(t => t.Slug == candidate, ct))
            {
                return candidate;
            }

            candidate = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..5]}";
        }

        return $"{baseSlug}-{Guid.NewGuid():N}";
    }
}
