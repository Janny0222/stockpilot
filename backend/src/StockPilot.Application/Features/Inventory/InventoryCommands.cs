using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Common;
using StockPilot.Domain.Inventory;

namespace StockPilot.Application.Features.Inventory;

public sealed record StockLine(Guid VariantId, decimal Quantity, decimal? UnitCost);

/// <summary>Manual stock-in or stock-out of one or more variants in one warehouse.</summary>
public sealed record RecordStockCommand(Guid WarehouseId, IReadOnlyList<StockLine> Lines, string? Reference, string? Note);

public sealed record AdjustStockCommand(Guid WarehouseId, Guid VariantId, decimal CountedQuantity, AdjustmentReason Reason, string? Note);

public sealed record TransferLine(Guid VariantId, decimal Quantity);

public sealed record TransferStockCommand(Guid SourceWarehouseId, Guid DestinationWarehouseId, IReadOnlyList<TransferLine> Lines, string? Note);

/// <summary>What the API returns after any stock operation: the ledger lines it created.</summary>
public sealed record StockOperationResult(Guid CorrelationId, string? DocumentNumber, IReadOnlyList<MovementResult> Movements);

public sealed record MovementResult(Guid WarehouseId, Guid VariantId, InventoryMovementType Type, decimal QuantityChange, decimal QuantityAfter);

internal static class QuantityRules
{
    public const decimal Max = 1_000_000_000m;

    /// <summary>Positive, below a sane maximum, at most 3 decimals (numeric(18,3)).</summary>
    public static IRuleBuilderOptions<T, decimal> ValidQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).WithMessage("Quantity must be greater than zero.")
            .LessThan(Max)
            .Must(q => decimal.Round(q, 3) == q).WithMessage("Use at most 3 decimal places.");
}

public sealed class RecordStockValidator : AbstractValidator<RecordStockCommand>
{
    public RecordStockValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one item.")
            .Must(l => l.Count <= 100).WithMessage("Up to 100 items per entry.")
            .Must(l => l.Select(x => x.VariantId).Distinct().Count() == l.Count).WithMessage("Each item can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.VariantId).NotEmpty();
            line.RuleFor(l => l.Quantity).ValidQuantity();
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).When(l => l.UnitCost.HasValue);
        });
        RuleFor(x => x.Reference).MaximumLength(InventoryTransaction.ReferenceMaxLength);
        RuleFor(x => x.Note).MaximumLength(InventoryTransaction.NoteMaxLength);
    }
}

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.VariantId).NotEmpty();
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0).LessThan(QuantityRules.Max)
            .Must(q => decimal.Round(q, 3) == q).WithMessage("Use at most 3 decimal places.");
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(InventoryTransaction.NoteMaxLength)
            .NotEmpty().When(x => x.Reason == AdjustmentReason.Other).WithMessage("Explain the adjustment when the reason is Other.");
    }
}

public sealed class TransferStockValidator : AbstractValidator<TransferStockCommand>
{
    public TransferStockValidator()
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.DestinationWarehouseId).NotEmpty()
            .NotEqual(x => x.SourceWarehouseId).WithMessage("Choose two different warehouses.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one item.")
            .Must(l => l.Count <= 100).WithMessage("Up to 100 items per transfer.")
            .Must(l => l.Select(x => x.VariantId).Distinct().Count() == l.Count).WithMessage("Each item can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.VariantId).NotEmpty();
            line.RuleFor(l => l.Quantity).ValidQuantity();
        });
        RuleFor(x => x.Note).MaximumLength(StockTransfer.NoteMaxLength);
    }
}

/// <summary>
/// Shared steps for every stock operation. The flow is always:
///   validate → check warehouse access → check variants → BEGIN → lock rows (ordered) → apply domain rules
///   → write ledger → COMMIT.
/// If any rule fails (e.g. insufficient stock), the whole transaction rolls back: no partial stock-out.
/// </summary>
public sealed class StockOperations(IAppDbContext db, ICurrentUser currentUser, WarehouseAccessService access, IStockLocker locker, TimeProvider clock)
{
    public Guid TenantId => currentUser.TenantId ?? throw new ForbiddenException("NO_TENANT", "Stock operations require a company account.");

    public async Task EnsureWarehouseUsableAsync(Guid warehouseId, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var isActive = await db.Warehouses
            .Where(w => w.Id == warehouseId && (allowed == null || allowed.Contains(w.Id)))
            .Select(w => (bool?)w.IsActive)
            .SingleOrDefaultAsync(ct);

        // Not found / another company / not assigned to this user → same 404 (no information leak).
        if (isActive is null)
        {
            throw new NotFoundException("Warehouse", warehouseId);
        }

        if (isActive == false)
        {
            throw new DomainException("WAREHOUSE_ARCHIVED", "This warehouse is archived. Restore it to move stock.");
        }
    }

    /// <summary>Variants must exist in this company, be active, and belong to an active product. Returns their cost prices.</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> EnsureVariantsUsableAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken ct)
    {
        var found = await db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .Join(db.Products, v => v.ProductId, p => p.Id, (v, p) => new { v.Id, v.Sku, v.IsActive, v.CostPrice, ProductActive = p.Status == ProductStatus.Active })
            .ToListAsync(ct);

        if (found.Count != variantIds.Count)
        {
            throw new DomainException("VARIANT_NOT_FOUND", "One or more items don't exist.");
        }

        var inactive = found.FirstOrDefault(v => !v.IsActive || !v.ProductActive);
        if (inactive is not null)
        {
            throw new DomainException("VARIANT_ARCHIVED", $"{inactive.Sku} is archived and can't be moved.");
        }

        return found.ToDictionary(v => v.Id, v => v.CostPrice);
    }

    public Task<bool> AllowNegativeStockAsync(CancellationToken ct) =>
        db.Tenants.Where(t => t.Id == TenantId).Select(t => t.AllowNegativeStock).SingleAsync(ct);

    public Task<IReadOnlyDictionary<(Guid WarehouseId, Guid VariantId), WarehouseStock>> LockAsync(
        IEnumerable<(Guid WarehouseId, Guid VariantId)> keys, CancellationToken ct) =>
        locker.LockAsync(TenantId, keys.ToList(), ct);

    public InventoryTransaction Ledger(
        WarehouseStock stock, InventoryMovementType type, decimal change, decimal unitCost, Guid correlationId,
        string? reference, string? note, AdjustmentReason? reason = null, string? sourceType = null, Guid? sourceId = null)
    {
        var entry = new InventoryTransaction(
            TenantId, stock.WarehouseId, stock.ProductVariantId, type, change, stock.QuantityOnHand, unitCost,
            correlationId, Clean(reference), Clean(note), reason, sourceType, sourceId, currentUser.UserId, clock.GetUtcNow());
        db.InventoryTransactions.Add(entry);
        return entry;
    }

    public static MovementResult ToResult(InventoryTransaction t) =>
        new(t.WarehouseId, t.ProductVariantId, t.Type, t.QuantityChange, t.QuantityAfter);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class StockInHandler(IAppDbContext db, IValidator<RecordStockCommand> validator, StockOperations ops)
{
    public async Task<StockOperationResult> Handle(RecordStockCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        await ops.EnsureWarehouseUsableAsync(command.WarehouseId, ct);
        var costPrices = await ops.EnsureVariantsUsableAsync(command.Lines.Select(l => l.VariantId).ToList(), ct);

        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var correlationId = Guid.CreateVersion7();
            var stocks = await ops.LockAsync(command.Lines.Select(l => (command.WarehouseId, l.VariantId)), tx);
            var entries = new List<InventoryTransaction>();

            foreach (var line in command.Lines)
            {
                // No cost entered → use the variant's catalogue cost price.
                var unitCost = line.UnitCost ?? costPrices[line.VariantId];
                var stock = stocks[(command.WarehouseId, line.VariantId)];
                stock.Receive(line.Quantity, unitCost);
                entries.Add(ops.Ledger(stock, InventoryMovementType.StockIn, line.Quantity, unitCost, correlationId, command.Reference, command.Note));
            }

            await db.SaveChangesAsync(tx);
            return new StockOperationResult(correlationId, null, entries.Select(StockOperations.ToResult).ToList());
        }, ct);
    }
}

public sealed class StockOutHandler(IAppDbContext db, IValidator<RecordStockCommand> validator, StockOperations ops, Notifications.LowStockAlerts alerts)
{
    public async Task<StockOperationResult> Handle(RecordStockCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        await ops.EnsureWarehouseUsableAsync(command.WarehouseId, ct);
        await ops.EnsureVariantsUsableAsync(command.Lines.Select(l => l.VariantId).ToList(), ct);
        var allowNegative = await ops.AllowNegativeStockAsync(ct);

        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var correlationId = Guid.CreateVersion7();
            var stocks = await ops.LockAsync(command.Lines.Select(l => (command.WarehouseId, l.VariantId)), tx);
            var entries = new List<InventoryTransaction>();

            foreach (var line in command.Lines)
            {
                var stock = stocks[(command.WarehouseId, line.VariantId)];
                stock.Issue(line.Quantity, allowNegative); // throws INSUFFICIENT_STOCK → whole entry rolls back
                entries.Add(ops.Ledger(stock, InventoryMovementType.StockOut, -line.Quantity, stock.AverageUnitCost, correlationId, command.Reference, command.Note));
            }

            await alerts.CheckAsync(stocks.Values, tx);
            await db.SaveChangesAsync(tx);
            return new StockOperationResult(correlationId, null, entries.Select(StockOperations.ToResult).ToList());
        }, ct);
    }
}

public sealed class AdjustStockHandler(IAppDbContext db, IValidator<AdjustStockCommand> validator, StockOperations ops, Notifications.LowStockAlerts alerts)
{
    public async Task<StockOperationResult> Handle(AdjustStockCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        await ops.EnsureWarehouseUsableAsync(command.WarehouseId, ct);
        var costPrices = await ops.EnsureVariantsUsableAsync([command.VariantId], ct);

        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var stocks = await ops.LockAsync([(command.WarehouseId, command.VariantId)], tx);
            var stock = stocks[(command.WarehouseId, command.VariantId)];
            var delta = stock.AdjustTo(command.CountedQuantity);

            if (delta == 0)
            {
                throw new DomainException("NO_CHANGE", "The counted quantity matches the system. Nothing to adjust.");
            }

            if (delta > 0)
            {
                stock.InitializeCostIfMissing(costPrices[command.VariantId]);
            }

            var entry = ops.Ledger(stock, delta > 0 ? InventoryMovementType.AdjustmentIncrease : InventoryMovementType.AdjustmentDecrease,
                delta, stock.AverageUnitCost, Guid.CreateVersion7(), null, command.Note, command.Reason);

            await alerts.CheckAsync(stocks.Values, tx);
            await db.SaveChangesAsync(tx);
            return new StockOperationResult(entry.CorrelationId, null, [StockOperations.ToResult(entry)]);
        }, ct);
    }
}

public sealed class TransferStockHandler(
    IAppDbContext db, IValidator<TransferStockCommand> validator, StockOperations ops, IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock,
    Notifications.LowStockAlerts alerts)
{
    public async Task<StockOperationResult> Handle(TransferStockCommand command, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        // Inventory Staff must be assigned to BOTH warehouses.
        await ops.EnsureWarehouseUsableAsync(command.SourceWarehouseId, ct);
        await ops.EnsureWarehouseUsableAsync(command.DestinationWarehouseId, ct);
        await ops.EnsureVariantsUsableAsync(command.Lines.Select(l => l.VariantId).ToList(), ct);
        var allowNegative = await ops.AllowNegativeStockAsync(ct);

        return await db.ExecuteInTransactionAsync(async tx =>
        {
            // Source AND destination rows are locked together, in one ordered statement: a transfer A→B running at
            // the same time as B→A can't deadlock.
            var keys = command.Lines.SelectMany(l => new[] { (command.SourceWarehouseId, l.VariantId), (command.DestinationWarehouseId, l.VariantId) });
            var stocks = await ops.LockAsync(keys, tx);

            var number = await numbers.NextAsync(ops.TenantId, "TR", tx);
            var transfer = new StockTransfer(ops.TenantId, number, command.SourceWarehouseId, command.DestinationWarehouseId, command.Note, currentUser.UserId, clock.GetUtcNow());
            var entries = new List<InventoryTransaction>();

            foreach (var line in command.Lines)
            {
                var source = stocks[(command.SourceWarehouseId, line.VariantId)];
                var destination = stocks[(command.DestinationWarehouseId, line.VariantId)];
                var unitCost = source.AverageUnitCost; // value travels with the stock

                source.Issue(line.Quantity, allowNegative);
                destination.Receive(line.Quantity, unitCost);
                transfer.AddItem(line.VariantId, line.Quantity, unitCost);

                entries.Add(ops.Ledger(source, InventoryMovementType.TransferOut, -line.Quantity, unitCost, transfer.Id, number, command.Note, sourceType: nameof(StockTransfer), sourceId: transfer.Id));
                entries.Add(ops.Ledger(destination, InventoryMovementType.TransferIn, line.Quantity, unitCost, transfer.Id, number, command.Note, sourceType: nameof(StockTransfer), sourceId: transfer.Id));
            }

            db.StockTransfers.Add(transfer);
            await alerts.CheckAsync(stocks.Values, tx); // only the source side can drop
            await db.SaveChangesAsync(tx);
            return new StockOperationResult(transfer.Id, number, entries.Select(StockOperations.ToResult).ToList());
        }, ct);
    }
}
