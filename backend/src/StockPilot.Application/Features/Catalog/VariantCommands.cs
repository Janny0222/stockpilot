using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Catalog;

namespace StockPilot.Application.Features.Catalog;

public sealed record UpdateVariantCommand(
    string Sku,
    string? Name,
    string? Barcode,
    decimal CostPrice,
    decimal SellingPrice,
    decimal ReorderLevel,
    uint Version)
{
    public VariantInput ToInput() => new(Sku, Name, Barcode, CostPrice, SellingPrice, ReorderLevel);
}

public sealed class UpdateVariantValidator : AbstractValidator<UpdateVariantCommand>
{
    // Same rules as VariantInputValidator, declared on this type so error keys stay "sku", "sellingPrice"…
    public UpdateVariantValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required.")
            .MaximumLength(ProductVariant.SkuMaxLength)
            .Matches("^[A-Za-z0-9._/-]+$").WithMessage("SKU can contain letters, numbers and . _ / - only.");
        RuleFor(x => x.Name).MaximumLength(ProductVariant.NameMaxLength);
        RuleFor(x => x.Barcode).MaximumLength(ProductVariant.BarcodeMaxLength)
            .Matches("^[A-Za-z0-9-]*$").WithMessage("Barcode can contain letters, numbers and - only.");
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000_000);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000_000);
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0).LessThan(1_000_000_000);
    }
}

public sealed class AddVariantHandler(IAppDbContext db, IValidator<VariantInput> validator)
{
    public async Task<Guid> Handle(Guid productId, VariantInput input, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        var product = await LoadAsync(db, productId, ct);
        await VariantRules.EnsureCodesAvailableAsync(db, [input], excludeVariantId: null, ct);

        var variant = product.AddVariant(input.Sku, input.Name, input.Barcode, input.CostPrice, input.SellingPrice, input.ReorderLevel);
        await db.SaveChangesAsync(ct);
        return variant.Id;
    }

    internal static async Task<Product> LoadAsync(IAppDbContext db, Guid productId, CancellationToken ct) =>
        await db.Products.Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == productId, ct)
        ?? throw new NotFoundException("Product", productId);
}

public sealed class UpdateVariantHandler(IAppDbContext db, IValidator<UpdateVariantCommand> validator, PermissionCheckerAccessor costAccess)
{
    public async Task Handle(Guid productId, Guid variantId, UpdateVariantCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var product = await AddVariantHandler.LoadAsync(db, productId, ct);
        var variant = product.FindVariant(variantId);
        await VariantRules.EnsureCodesAvailableAsync(db, [command.ToInput()], excludeVariantId: variantId, ct);

        db.Entry(variant).Property(v => v.Version).OriginalValue = command.Version;

        // Someone who can't SEE cost prices mustn't be able to OVERWRITE them blind.
        var cost = await costAccess.CanViewCostAsync(ct) ? command.CostPrice : variant.CostPrice;
        variant.Update(command.Sku, command.Name, command.Barcode, cost, command.SellingPrice, command.ReorderLevel);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SetVariantStatusHandler(IAppDbContext db)
{
    public async Task Handle(Guid productId, Guid variantId, bool archive, CancellationToken ct)
    {
        var product = await AddVariantHandler.LoadAsync(db, productId, ct);

        if (archive)
        {
            product.ArchiveVariant(variantId);
        }
        else
        {
            product.RestoreVariant(variantId);
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Small wrapper so handlers can ask "may this user see costs?" without repeating the permission code.</summary>
public sealed class PermissionCheckerAccessor(Common.Security.PermissionChecker checker)
{
    public Task<bool> CanViewCostAsync(CancellationToken ct) => checker.HasAsync(Domain.Identity.Permissions.Inventory.ViewCost, ct);
}
