using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Security;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Catalog;

public sealed record VariantInput(
    string Sku,
    string? Name,
    string? Barcode,
    decimal CostPrice,
    decimal SellingPrice,
    decimal ReorderLevel);

public sealed record CreateProductCommand(
    string Name,
    string? Description,
    Guid? CategoryId,
    string UnitOfMeasure,
    IReadOnlyList<VariantInput> Variants);

public sealed record UpdateProductCommand(
    string Name,
    string? Description,
    Guid? CategoryId,
    string UnitOfMeasure,
    uint Version);

public sealed class VariantInputValidator : AbstractValidator<VariantInput>
{
    public VariantInputValidator()
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

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(x => x.UnitOfMeasure).Must(UnitsOfMeasure.IsValid).WithMessage("Choose a supported unit.");
        RuleFor(x => x.Variants).NotEmpty().WithMessage("Add at least one variant.")
            .Must(v => v.Count <= 50).WithMessage("A product can have at most 50 variants.");
        RuleForEach(x => x.Variants).SetValidator(new VariantInputValidator());
        RuleFor(x => x.Variants)
            .Must(v => v.Select(x => ProductVariant.NormalizeSku(x.Sku ?? "")).Distinct().Count() == v.Count)
            .WithMessage("Each variant needs a different SKU.");
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(x => x.UnitOfMeasure).Must(UnitsOfMeasure.IsValid).WithMessage("Choose a supported unit.");
    }
}

public sealed class CreateProductHandler(
    IAppDbContext db, IValidator<CreateProductCommand> validator, ICurrentUser currentUser, PermissionChecker permissions)
{
    public async Task<ProductDetailDto> Handle(CreateProductCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var category = await CategoryLookup.FindParentAsync(db, command.CategoryId, ct);
        await VariantRules.EnsureCodesAvailableAsync(db, command.Variants, excludeVariantId: null, ct);

        var product = new Product(currentUser.TenantId!.Value, command.Name, command.Description, category?.Id, command.UnitOfMeasure);
        foreach (var v in command.Variants)
        {
            product.AddVariant(v.Sku, v.Name, v.Barcode, v.CostPrice, v.SellingPrice, v.ReorderLevel);
        }

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return ProductMapper.ToDetail(product, await permissions.HasAsync(Permissions.Inventory.ViewCost, ct)) with
        {
            CategoryName = category?.Name,
        };
    }
}

public sealed class UpdateProductHandler(IAppDbContext db, IValidator<UpdateProductCommand> validator)
{
    public async Task Handle(Guid id, UpdateProductCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product", id);
        var category = await CategoryLookup.FindParentAsync(db, command.CategoryId, ct);

        // Optimistic concurrency: compare against the version the user loaded, not the one we just read.
        // If someone saved in between, SaveChanges throws → 409 "changed by someone else".
        db.Entry(product).Property(p => p.Version).OriginalValue = command.Version;

        product.UpdateDetails(command.Name, command.Description, category?.Id, command.UnitOfMeasure);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SetProductStatusHandler(IAppDbContext db)
{
    public async Task Handle(Guid id, bool archive, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product", id);

        if (archive)
        {
            product.Archive();
        }
        else
        {
            product.Restore();
        }

        await db.SaveChangesAsync(ct);
    }
}

internal static class VariantRules
{
    /// <summary>
    /// Friendly duplicate check before saving. The unique indexes are still the real guarantee
    /// (two simultaneous creates can both pass this check; the database rejects the second).
    /// </summary>
    public static async Task EnsureCodesAvailableAsync(IAppDbContext db, IReadOnlyList<VariantInput> variants, Guid? excludeVariantId, CancellationToken ct)
    {
        var skus = variants.Select(v => ProductVariant.NormalizeSku(v.Sku)).ToList();
        var takenSku = await db.ProductVariants
            .Where(v => skus.Contains(v.Sku) && v.Id != excludeVariantId)
            .Select(v => v.Sku)
            .FirstOrDefaultAsync(ct);
        if (takenSku is not null)
        {
            throw new ConflictException("SKU_TAKEN", $"SKU '{takenSku}' is already used by another product.");
        }

        var barcodes = variants.Where(v => !string.IsNullOrWhiteSpace(v.Barcode)).Select(v => v.Barcode!.Trim()).ToList();
        if (barcodes.Count == 0)
        {
            return;
        }

        var takenBarcode = await db.ProductVariants
            .Where(v => v.Barcode != null && barcodes.Contains(v.Barcode) && v.Id != excludeVariantId)
            .Select(v => v.Barcode)
            .FirstOrDefaultAsync(ct);
        if (takenBarcode is not null)
        {
            throw new ConflictException("BARCODE_TAKEN", $"Barcode '{takenBarcode}' is already used by another product.");
        }
    }
}
