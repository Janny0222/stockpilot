using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Domain.Sales;

namespace StockPilot.Application.Features.Sales;

public sealed record CustomerDto(
    Guid Id, string Code, string Name, string? ContactName, string? Email, string? Phone, string? Address,
    int PaymentTermsDays, string? Notes, bool IsActive, int OpenOrders, decimal TotalSales);

public sealed record SaveCustomerCommand(
    string Code, string Name, string? ContactName, string? Email, string? Phone, string? Address, int PaymentTermsDays, string? Notes);

public sealed class SaveCustomerValidator : AbstractValidator<SaveCustomerCommand>
{
    public SaveCustomerValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Customer.CodeMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code can contain letters, numbers and - only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(100);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed record ListCustomersQuery : PageRequest
{
    public bool? IsActive { get; init; }
}

public sealed class ListCustomersHandler(IAppDbContext db)
{
    public async Task<PagedResult<CustomerDto>> Handle(ListCustomersQuery query, CancellationToken ct)
    {
        var customers = db.Customers.AsNoTracking();

        if (query.IsActive is { } active) customers = customers.Where(c => c.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            customers = customers.Where(c => c.Name.ToLower().Contains(term) || c.Code.ToLower().Contains(term)
                || (c.ContactName != null && c.ContactName.ToLower().Contains(term)));
        }

        customers = query.ParseSort() switch
        {
            ("code", false) => customers.OrderBy(c => c.Code),
            ("code", true) => customers.OrderByDescending(c => c.Code),
            ("name", true) => customers.OrderByDescending(c => c.Name),
            _ => customers.OrderBy(c => c.Name),
        };

        return await customers.Select(Project(db)).ToPagedResultAsync(query, ct);
    }

    /// <summary>Total sales = value actually SHIPPED (revenue), not what was merely ordered.</summary>
    internal static System.Linq.Expressions.Expression<Func<Customer, CustomerDto>> Project(IAppDbContext db) => c =>
        new CustomerDto(
            c.Id, c.Code, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.PaymentTermsDays, c.Notes, c.IsActive,
            db.SalesOrders.Count(o => o.CustomerId == c.Id
                && (o.Status == SalesOrderStatus.Confirmed || o.Status == SalesOrderStatus.PartiallyFulfilled)),
            db.SalesOrderItems.Where(i => db.SalesOrders.Any(o => o.Id == i.SalesOrderId && o.CustomerId == c.Id))
                .Sum(i => (decimal?)(i.QuantityShipped * i.UnitPrice)) ?? 0);
}

public sealed class GetCustomerHandler(IAppDbContext db)
{
    public async Task<CustomerDto> Handle(Guid id, CancellationToken ct) =>
        await db.Customers.AsNoTracking().Where(c => c.Id == id).Select(ListCustomersHandler.Project(db)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Customer", id);
}

public sealed class CreateCustomerHandler(IAppDbContext db, IValidator<SaveCustomerCommand> validator, ICurrentUser currentUser)
{
    public async Task<Guid> Handle(SaveCustomerCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        var customer = new Customer(currentUser.TenantId!.Value, c.Code, c.Name);
        customer.Update(c.Code, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.PaymentTermsDays, c.Notes);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct); // duplicate code → 409 CUSTOMER_CODE_TAKEN (unique index)
        return customer.Id;
    }
}

public sealed class UpdateCustomerHandler(IAppDbContext db, IValidator<SaveCustomerCommand> validator)
{
    public async Task Handle(Guid id, SaveCustomerCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        customer.Update(c.Code, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.PaymentTermsDays, c.Notes);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SetCustomerStatusHandler(IAppDbContext db)
{
    public async Task Handle(Guid id, bool archive, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        if (archive) customer.Archive();
        else customer.Restore();
        await db.SaveChangesAsync(ct);
    }
}
