using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Common;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Application.Features.Warehouses;

public sealed record SaveWarehouseCommand(string Code, string Name, string? AddressLine, string? City, string? Country);

public sealed class SaveWarehouseValidator : AbstractValidator<SaveWarehouseCommand>
{
    public SaveWarehouseValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Warehouse.CodeMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Code can contain letters, numbers and - only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Warehouse.NameMaxLength);
        RuleFor(x => x.AddressLine).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
    }
}

public sealed class CreateWarehouseHandler(IAppDbContext db, IValidator<SaveWarehouseCommand> validator, ICurrentUser currentUser)
{
    public async Task<WarehouseDto> Handle(SaveWarehouseCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var warehouse = new Warehouse(currentUser.TenantId!.Value, command.Code, command.Name, isDefault: false);
        warehouse.SetAddress(command.AddressLine, command.City, command.Country);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(ct); // duplicate code → unique index → 409 WAREHOUSE_CODE_TAKEN
        return ListWarehousesHandler.ToDto.Compile()(warehouse);
    }
}

public sealed class UpdateWarehouseHandler(IAppDbContext db, IValidator<SaveWarehouseCommand> validator)
{
    public async Task<WarehouseDto> Handle(Guid id, SaveWarehouseCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Warehouse", id);
        warehouse.Update(command.Code, command.Name);
        warehouse.SetAddress(command.AddressLine, command.City, command.Country);
        await db.SaveChangesAsync(ct);
        return ListWarehousesHandler.ToDto.Compile()(warehouse);
    }
}

public sealed class SetWarehouseStatusHandler(IAppDbContext db)
{
    public async Task Handle(Guid id, bool archive, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Warehouse", id);

        if (archive)
        {
            // A warehouse with stock can't disappear from the books: move or adjust the stock out first.
            var hasStock = await db.WarehouseStocks.AnyAsync(s => s.WarehouseId == id && (s.QuantityOnHand != 0 || s.QuantityReserved != 0), ct);
            if (hasStock)
            {
                throw new DomainException("WAREHOUSE_HAS_STOCK", "This warehouse still holds stock. Transfer or adjust it to zero before archiving.");
            }

            warehouse.Archive();
        }
        else
        {
            warehouse.Restore();
        }

        await db.SaveChangesAsync(ct);
    }
}

public sealed class SetDefaultWarehouseHandler(IAppDbContext db)
{
    public async Task Handle(Guid id, CancellationToken ct)
    {
        var all = await db.Warehouses.ToListAsync(ct);
        var target = all.SingleOrDefault(w => w.Id == id) ?? throw new NotFoundException("Warehouse", id);
        Warehouse.MakeDefault(target, all);
        await db.SaveChangesAsync(ct);
    }
}
