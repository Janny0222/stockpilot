using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Common;

namespace StockPilot.Application.Features.Catalog;

public sealed record CategoryDto(Guid Id, string Name, string? Description, Guid? ParentId, int ProductCount);

public sealed record SaveCategoryCommand(string Name, string? Description, Guid? ParentId);

public sealed class SaveCategoryValidator : AbstractValidator<SaveCategoryCommand>
{
    public SaveCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProductCategory.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ProductCategory.DescriptionMaxLength);
    }
}

/// <summary>All categories of the company (small list; no paging). Ordered parents-first for tree display.</summary>
public sealed class ListCategoriesHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(CancellationToken ct) =>
        await db.ProductCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id, c.Name, c.Description, c.ParentId,
                db.Products.Count(p => p.CategoryId == c.Id && p.Status == ProductStatus.Active)))
            .ToListAsync(ct);
}

public sealed class CreateCategoryHandler(IAppDbContext db, IValidator<SaveCategoryCommand> validator, ICurrentUser currentUser)
{
    public async Task<CategoryDto> Handle(SaveCategoryCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var parent = await CategoryLookup.FindParentAsync(db, command.ParentId, ct);

        var category = new ProductCategory(currentUser.TenantId!.Value, command.Name, command.Description, parent);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return new CategoryDto(category.Id, category.Name, category.Description, category.ParentId, 0);
    }
}

public sealed class UpdateCategoryHandler(IAppDbContext db, IValidator<SaveCategoryCommand> validator)
{
    public async Task<CategoryDto> Handle(Guid id, SaveCategoryCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);

        var category = await db.ProductCategories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category", id);
        var parent = await CategoryLookup.FindParentAsync(db, command.ParentId, ct);

        if (parent is not null && await db.ProductCategories.AnyAsync(c => c.ParentId == id, ct))
        {
            throw new DomainException("CATEGORY_TOO_DEEP", "This category has subcategories, so it can't be moved under another one.");
        }

        category.Update(command.Name, command.Description, parent);
        await db.SaveChangesAsync(ct);

        var count = await db.Products.CountAsync(p => p.CategoryId == id && p.Status == ProductStatus.Active, ct);
        return new CategoryDto(category.Id, category.Name, category.Description, category.ParentId, count);
    }
}

/// <summary>Only empty categories can be deleted: products and subcategories must be moved first.</summary>
public sealed class DeleteCategoryHandler(IAppDbContext db)
{
    public async Task Handle(Guid id, CancellationToken ct)
    {
        var category = await db.ProductCategories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Category", id);

        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
        {
            throw new DomainException("CATEGORY_IN_USE", "Move or recategorise this category's products before deleting it.");
        }

        if (await db.ProductCategories.AnyAsync(c => c.ParentId == id, ct))
        {
            throw new DomainException("CATEGORY_HAS_CHILDREN", "Delete or move its subcategories first.");
        }

        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }
}

internal static class CategoryLookup
{
    /// <summary>
    /// Resolves a category ID from request input. The tenant query filter makes another company's
    /// category ID behave exactly like a non-existent one, so it can't be attached (IDOR protection).
    /// </summary>
    public static async Task<ProductCategory?> FindParentAsync(IAppDbContext db, Guid? id, CancellationToken ct)
    {
        if (id is null)
        {
            return null;
        }

        return await db.ProductCategories.SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new DomainException("CATEGORY_NOT_FOUND", "The selected category doesn't exist.");
    }
}
