using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Common.Security;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Purchasing;

namespace StockPilot.Application.Features.Purchasing;

public sealed record SupplierDto(
    Guid Id, string Code, string Name, string? ContactName, string? Email, string? Phone, string? Address,
    int PaymentTermsDays, string? Notes, bool IsActive, int OpenOrders, decimal? TotalPurchased);

public sealed record SaveSupplierCommand(
    string Code, string Name, string? ContactName, string? Email, string? Phone, string? Address, int PaymentTermsDays, string? Notes);

public sealed class SaveSupplierValidator : AbstractValidator<SaveSupplierCommand>
{
    public SaveSupplierValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Supplier.CodeMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code can contain letters, numbers and - only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Supplier.NameMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed record ListSuppliersQuery : PageRequest
{
    public bool? IsActive { get; init; }
}

public sealed class ListSuppliersHandler(IAppDbContext db, PermissionChecker permissions)
{
    public async Task<PagedResult<SupplierDto>> Handle(ListSuppliersQuery query, CancellationToken ct)
    {
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);
        var suppliers = db.Suppliers.AsNoTracking();

        if (query.IsActive is { } active) suppliers = suppliers.Where(s => s.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            suppliers = suppliers.Where(s => s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term)
                || (s.ContactName != null && s.ContactName.ToLower().Contains(term)));
        }

        suppliers = query.ParseSort() switch
        {
            ("code", false) => suppliers.OrderBy(s => s.Code),
            ("code", true) => suppliers.OrderByDescending(s => s.Code),
            ("name", true) => suppliers.OrderByDescending(s => s.Name),
            _ => suppliers.OrderBy(s => s.Name),
        };

        return await suppliers.Select(Project(db, canViewCost)).ToPagedResultAsync(query, ct);
    }

    internal static System.Linq.Expressions.Expression<Func<Supplier, SupplierDto>> Project(IAppDbContext db, bool canViewCost) => s =>
        new SupplierDto(
            s.Id, s.Code, s.Name, s.ContactName, s.Email, s.Phone, s.Address, s.PaymentTermsDays, s.Notes, s.IsActive,
            db.PurchaseOrders.Count(p => p.SupplierId == s.Id
                && (p.Status == PurchaseOrderStatus.Submitted || p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived)),
            canViewCost
                ? db.PurchaseOrderItems.Where(i => db.PurchaseOrders.Any(p => p.Id == i.PurchaseOrderId && p.SupplierId == s.Id))
                    .Sum(i => (decimal?)(i.QuantityReceived * i.UnitCost)) ?? 0
                : null);
}

public sealed class GetSupplierHandler(IAppDbContext db, PermissionChecker permissions)
{
    public async Task<SupplierDto> Handle(Guid id, CancellationToken ct)
    {
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);
        return await db.Suppliers.AsNoTracking().Where(s => s.Id == id).Select(ListSuppliersHandler.Project(db, canViewCost)).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Supplier", id);
    }
}

public sealed class CreateSupplierHandler(IAppDbContext db, IValidator<SaveSupplierCommand> validator, ICurrentUser currentUser)
{
    public async Task<Guid> Handle(SaveSupplierCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        var supplier = new Supplier(currentUser.TenantId!.Value, c.Code, c.Name);
        supplier.Update(c.Code, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.PaymentTermsDays, c.Notes);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct); // duplicate code → 409 SUPPLIER_CODE_TAKEN (unique index)
        return supplier.Id;
    }
}

public sealed class UpdateSupplierHandler(IAppDbContext db, IValidator<SaveSupplierCommand> validator)
{
    public async Task Handle(Guid id, SaveSupplierCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Supplier", id);
        supplier.Update(c.Code, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.PaymentTermsDays, c.Notes);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SetSupplierStatusHandler(IAppDbContext db)
{
    public async Task Handle(Guid id, bool archive, CancellationToken ct)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Supplier", id);
        if (archive) supplier.Archive();
        else supplier.Restore();
        await db.SaveChangesAsync(ct);
    }
}
