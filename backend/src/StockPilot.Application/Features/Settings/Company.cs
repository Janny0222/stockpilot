using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Tenancy;

namespace StockPilot.Application.Features.Settings;

/// <summary>CurrencyLocked is true once stock has moved: every stored cost and price is in the current currency.</summary>
public sealed record CompanySettingsDto(
    Guid Id, string Name, string Slug, string Currency, string TimeZone, bool AllowNegativeStock, bool CurrencyLocked,
    DateTimeOffset CreatedAt);

public sealed record UpdateCompanySettingsCommand(string Name, string Currency, string TimeZone, bool AllowNegativeStock);

public sealed class UpdateCompanySettingsValidator : AbstractValidator<UpdateCompanySettingsCommand>
{
    public UpdateCompanySettingsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Currency).NotEmpty().Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a 3-letter ISO code.");
        // Same shape check as registration ("Asia/Manila", "UTC"); ReportClock falls back to UTC for an unknown zone.
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(64).Matches(@"^[A-Za-z_]+(/[A-Za-z0-9_+\-]+)*$")
            .WithMessage("Time zone must be an IANA name such as Asia/Manila.");
    }
}

public sealed class GetCompanySettingsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<CompanySettingsDto> Handle(CancellationToken ct)
    {
        var tenantId = currentUser.TenantId ?? throw new ForbiddenException("NO_TENANT", "This account has no company.");
        var currencyLocked = await db.InventoryTransactions.AnyAsync(ct);
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new CompanySettingsDto(t.Id, t.Name, t.Slug, t.Currency, t.TimeZone, t.AllowNegativeStock, currencyLocked, t.CreatedAt))
            .SingleAsync(ct);
    }
}

public sealed class UpdateCompanySettingsHandler(
    IAppDbContext db, IValidator<UpdateCompanySettingsCommand> validator, ICurrentUser currentUser, GetCompanySettingsHandler get)
{
    public async Task<CompanySettingsDto> Handle(UpdateCompanySettingsCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var tenantId = currentUser.TenantId ?? throw new ForbiddenException("NO_TENANT", "This account has no company.");
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, ct);

        var currency = command.Currency.ToUpperInvariant();
        if (currency != tenant.Currency && await db.InventoryTransactions.AnyAsync(ct))
        {
            throw new ConflictException(
                "CURRENCY_LOCKED", "The currency can't change after stock has moved: every cost and price is stored in it.");
        }

        tenant.UpdateProfile(command.Name, currency, command.TimeZone, command.AllowNegativeStock);
        await db.SaveChangesAsync(ct); // audited automatically (Tenant Updated, old → new)
        return await get.Handle(ct);
    }
}
