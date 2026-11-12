using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Common.Security;
using StockPilot.Application.Features.Inventory;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Purchasing;

namespace StockPilot.Application.Features.Purchasing;

// ======================= Contracts =======================

public sealed record PurchaseLineInput(Guid VariantId, decimal Quantity, decimal UnitCost);

public sealed record SavePurchaseOrderCommand(
    Guid SupplierId, Guid WarehouseId, DateOnly OrderDate, DateOnly? ExpectedDate, string? Notes,
    IReadOnlyList<PurchaseLineInput> Lines, uint? Version);

public sealed record CancelPurchaseOrderCommand(string Reason);

public sealed record ReceiveLineInput(Guid ItemId, decimal Quantity);

public sealed record ReceivePurchaseOrderCommand(IReadOnlyList<ReceiveLineInput> Lines, string? Note);

public sealed record ReceiptResult(Guid ReceiptId, string ReceiptNumber, PurchaseOrderStatus Status);

public sealed record PurchaseOrderListItemDto(
    Guid Id, string Number, Guid SupplierId, string SupplierName, Guid WarehouseId, string WarehouseCode,
    PurchaseOrderStatus Status, DateOnly OrderDate, DateOnly? ExpectedDate, int ItemCount,
    decimal QuantityOrdered, decimal QuantityReceived, decimal? Total);

public sealed record PurchaseOrderItemDto(
    Guid Id, Guid VariantId, Guid ProductId, string ProductName, string Sku, string? VariantName, string UnitOfMeasure,
    decimal QuantityOrdered, decimal QuantityReceived, decimal QuantityRemaining, decimal? UnitCost, decimal? LineTotal);

public sealed record ReceiptItemDto(string Sku, string ProductName, decimal Quantity);

public sealed record ReceiptDto(Guid Id, string Number, DateTimeOffset ReceivedAt, string? ReceivedBy, string? Note, IReadOnlyList<ReceiptItemDto> Items);

public sealed record PurchaseOrderDetailDto(
    Guid Id, string Number, PurchaseOrderStatus Status, Guid SupplierId, string SupplierName, Guid WarehouseId, string WarehouseName,
    DateOnly OrderDate, DateOnly? ExpectedDate, string? Notes, decimal? Total, uint Version, bool CanViewCost,
    string? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt, string? ApprovedBy,
    DateTimeOffset? CancelledAt, string? CancelReason,
    IReadOnlyList<PurchaseOrderItemDto> Items, IReadOnlyList<ReceiptDto> Receipts);

public sealed record ListPurchaseOrdersQuery : PageRequest
{
    public PurchaseOrderStatus? Status { get; init; }
    public Guid? SupplierId { get; init; }
    public Guid? WarehouseId { get; init; }
}

// ======================= Validation =======================

public sealed class SavePurchaseOrderValidator : AbstractValidator<SavePurchaseOrderCommand>
{
    public SavePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("Choose a supplier.");
        RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("Choose the warehouse to deliver to.");
        RuleFor(x => x.ExpectedDate).GreaterThanOrEqualTo(x => x.OrderDate).When(x => x.ExpectedDate.HasValue)
            .WithMessage("The expected date can't be before the order date.");
        RuleFor(x => x.Notes).MaximumLength(PurchaseOrder.NotesMaxLength);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one item.")
            .Must(l => l.Count <= PurchaseOrder.MaxItems).WithMessage($"Up to {PurchaseOrder.MaxItems} items per order.")
            .Must(l => l.Select(x => x.VariantId).Distinct().Count() == l.Count).WithMessage("Each item can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).ValidQuantity();
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).LessThan(1_000_000_000);
        });
    }
}

public sealed class ReceivePurchaseOrderValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Enter what arrived.")
            .Must(l => l.Any(x => x.Quantity > 0)).WithMessage("Enter a quantity for at least one item.")
            .Must(l => l.Select(x => x.ItemId).Distinct().Count() == l.Count).WithMessage("Each line can appear only once.");
        RuleForEach(x => x.Lines).ChildRules(line =>
            line.RuleFor(l => l.Quantity).GreaterThanOrEqualTo(0).LessThan(1_000_000_000)
                .Must(q => decimal.Round(q, 3) == q).WithMessage("Use at most 3 decimal places."));
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

// ======================= Queries =======================

public sealed class ListPurchaseOrdersHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions)
{
    public async Task<PagedResult<PurchaseOrderListItemDto>> Handle(ListPurchaseOrdersQuery query, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        var rows =
            from p in db.PurchaseOrders.AsNoTracking()
            join s in db.Suppliers on p.SupplierId equals s.Id
            join w in db.Warehouses on p.WarehouseId equals w.Id
            select new { p, s, w };

        if (allowed is not null) rows = rows.Where(r => allowed.Contains(r.w.Id));
        if (query.Status is { } status) rows = rows.Where(r => r.p.Status == status);
        if (query.SupplierId is { } supplierId) rows = rows.Where(r => r.p.SupplierId == supplierId);
        if (query.WarehouseId is { } warehouseId) rows = rows.Where(r => r.p.WarehouseId == warehouseId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(r => r.p.Number.ToLower().Contains(term) || r.s.Name.ToLower().Contains(term));
        }

        rows = query.ParseSort() switch
        {
            // Like the hidden Total column: no sorting by cost for roles that can't see it.
            ("total", true) when canViewCost => rows.OrderByDescending(r => r.p.Total),
            ("total", false) when canViewCost => rows.OrderBy(r => r.p.Total),
            ("expected", false) => rows.OrderBy(r => r.p.ExpectedDate),
            ("expected", true) => rows.OrderByDescending(r => r.p.ExpectedDate),
            ("number", false) => rows.OrderBy(r => r.p.Number),
            _ => rows.OrderByDescending(r => r.p.Number),
        };

        return await rows
            .Select(r => new PurchaseOrderListItemDto(
                r.p.Id, r.p.Number, r.s.Id, r.s.Name, r.w.Id, r.w.Code, r.p.Status, r.p.OrderDate, r.p.ExpectedDate,
                r.p.Items.Count, r.p.Items.Sum(i => i.QuantityOrdered), r.p.Items.Sum(i => i.QuantityReceived),
                canViewCost ? r.p.Total : null))
            .ToPagedResultAsync(query, ct);
    }
}

public sealed class GetPurchaseOrderHandler(IAppDbContext db, WarehouseAccessService access, PermissionChecker permissions)
{
    public async Task<PurchaseOrderDetailDto> Handle(Guid id, CancellationToken ct)
    {
        var allowed = await access.GetAccessibleWarehouseIdsAsync(ct);
        var canViewCost = await permissions.HasAsync(Permissions.Inventory.ViewCost, ct);

        var header = await (
            from p in db.PurchaseOrders.AsNoTracking()
            join s in db.Suppliers on p.SupplierId equals s.Id
            join w in db.Warehouses on p.WarehouseId equals w.Id
            join cu in db.Users on p.CreatedBy equals cu.Id into creators
            from cu in creators.DefaultIfEmpty()
            join au in db.Users on p.ApprovedBy equals au.Id into approvers
            from au in approvers.DefaultIfEmpty()
            where p.Id == id && (allowed == null || allowed.Contains(w.Id))
            select new
            {
                p, SupplierName = s.Name, WarehouseName = w.Name,
                CreatedBy = cu != null ? cu.FirstName + " " + cu.LastName : null,
                ApprovedBy = au != null ? au.FirstName + " " + au.LastName : null,
            }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Purchase order", id);

        var items = await (
            from i in db.PurchaseOrderItems.AsNoTracking()
            join v in db.ProductVariants on i.ProductVariantId equals v.Id
            join pr in db.Products on v.ProductId equals pr.Id
            where i.PurchaseOrderId == id
            orderby pr.Name, v.Sku
            select new PurchaseOrderItemDto(
                i.Id, v.Id, pr.Id, pr.Name, v.Sku, v.Name, pr.UnitOfMeasure, i.QuantityOrdered, i.QuantityReceived,
                i.QuantityOrdered - i.QuantityReceived, canViewCost ? i.UnitCost : null, canViewCost ? i.LineTotal : null))
            .ToListAsync(ct);

        var receiptRows = await (
            from r in db.PurchaseReceipts.AsNoTracking()
            join u in db.Users on r.ReceivedBy equals u.Id into users
            from u in users.DefaultIfEmpty()
            where r.PurchaseOrderId == id
            orderby r.ReceivedAt
            select new { r.Id, r.Number, r.ReceivedAt, By = u != null ? u.FirstName + " " + u.LastName : null, r.Note })
            .ToListAsync(ct);

        var receiptIds = receiptRows.Select(r => r.Id).ToList();
        var receiptItems = await (
            from ri in db.PurchaseReceiptItems.AsNoTracking()
            join v in db.ProductVariants on ri.ProductVariantId equals v.Id
            join pr in db.Products on v.ProductId equals pr.Id
            where receiptIds.Contains(ri.PurchaseReceiptId)
            select new { ri.PurchaseReceiptId, v.Sku, pr.Name, ri.Quantity })
            .ToListAsync(ct);

        var receipts = receiptRows.Select(r => new ReceiptDto(r.Id, r.Number, r.ReceivedAt, r.By, r.Note,
            receiptItems.Where(i => i.PurchaseReceiptId == r.Id).Select(i => new ReceiptItemDto(i.Sku, i.Name, i.Quantity)).ToList())).ToList();

        var po = header.p;
        return new PurchaseOrderDetailDto(
            po.Id, po.Number, po.Status, po.SupplierId, header.SupplierName, po.WarehouseId, header.WarehouseName,
            po.OrderDate, po.ExpectedDate, po.Notes, canViewCost ? po.Total : null, po.Version, canViewCost,
            header.CreatedBy, po.CreatedAt, po.SubmittedAt, po.ApprovedAt, header.ApprovedBy, po.CancelledAt, po.CancelReason,
            items, receipts);
    }
}

// ======================= Commands =======================

internal static class PurchaseRules
{
    public static async Task EnsureSupplierAndWarehouseAsync(IAppDbContext db, Guid supplierId, Guid warehouseId, CancellationToken ct)
    {
        // Tenant query filters make another company's IDs look exactly like missing ones (IDOR-safe).
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, ct))
        {
            throw new DomainException("SUPPLIER_NOT_FOUND", "Choose an active supplier.");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.IsActive, ct))
        {
            throw new DomainException("WAREHOUSE_NOT_FOUND", "Choose an active warehouse.");
        }
    }

    public static async Task<PurchaseOrder> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.PurchaseOrders.Include(p => p.Items).SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException("Purchase order", id);
}

public sealed class CreatePurchaseOrderHandler(
    IAppDbContext db, IValidator<SavePurchaseOrderCommand> validator, StockOperations ops, IDocumentNumberGenerator numbers)
{
    public async Task<Guid> Handle(SavePurchaseOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        await PurchaseRules.EnsureSupplierAndWarehouseAsync(db, c.SupplierId, c.WarehouseId, ct);
        await ops.EnsureVariantsUsableAsync(c.Lines.Select(l => l.VariantId).ToList(), ct);

        // Number + insert in one transaction: a failed save never burns a PO number.
        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var number = await numbers.NextAsync(ops.TenantId, "PO", tx);
            var po = new PurchaseOrder(ops.TenantId, number, c.SupplierId, c.WarehouseId, c.OrderDate, c.ExpectedDate, c.Notes);
            po.ReplaceItems(c.Lines.Select(l => (l.VariantId, l.Quantity, l.UnitCost)));
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync(tx);
            return po.Id;
        }, ct);
    }
}

public sealed class UpdatePurchaseOrderHandler(IAppDbContext db, IValidator<SavePurchaseOrderCommand> validator, StockOperations ops)
{
    public async Task Handle(Guid id, SavePurchaseOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);
        await PurchaseRules.EnsureSupplierAndWarehouseAsync(db, c.SupplierId, c.WarehouseId, ct);
        await ops.EnsureVariantsUsableAsync(c.Lines.Select(l => l.VariantId).ToList(), ct);

        var po = await PurchaseRules.LoadAsync(db, id, ct);
        if (c.Version is { } version)
        {
            db.Entry(po).Property(p => p.Version).OriginalValue = version; // stale edit → 409
        }

        po.SetHeader(c.SupplierId, c.WarehouseId, c.OrderDate, c.ExpectedDate, c.Notes);
        po.ReplaceItems(c.Lines.Select(l => (l.VariantId, l.Quantity, l.UnitCost)));
        await db.SaveChangesAsync(ct);
    }
}

public enum PurchaseTransition
{
    Submit,
    Approve,
}

public sealed class TransitionPurchaseOrderHandler(
    IAppDbContext db, ICurrentUser currentUser, TimeProvider clock, Notifications.NotificationPublisher notifications)
{
    public async Task Handle(Guid id, PurchaseTransition transition, CancellationToken ct)
    {
        var po = await PurchaseRules.LoadAsync(db, id, ct);
        var now = clock.GetUtcNow();

        if (transition == PurchaseTransition.Submit)
        {
            po.Submit(currentUser.UserId, now);
        }
        else
        {
            po.Approve(currentUser.UserId, now);
        }

        var info = await (
            from s in db.Suppliers
            where s.Id == po.SupplierId
            select new
            {
                Supplier = s.Name,
                Warehouse = db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).Single(),
                By = db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.FirstName + " " + u.LastName).SingleOrDefault(),
            }).SingleAsync(ct);
        var by = info.By ?? "a teammate";

        // Saved together with the status change: the notification exists exactly when the transition does.
        if (transition == PurchaseTransition.Submit)
        {
            await notifications.ToAudienceAsync(
                Domain.Notifications.NotificationType.PurchaseOrderAwaitingApproval, warehouseId: null,
                new($"{po.Number} needs your approval",
                    // No amount: the audience is everyone who can approve, and amounts are cost data (inventory.view_cost).
                    $"{info.Supplier}, {po.Items.Count} {(po.Items.Count == 1 ? "line" : "lines")}, for {info.Warehouse}. Submitted by {by}.",
                    "PurchaseOrder", po.Id),
                includeActor: false, ct);
        }
        else
        {
            var expected = po.ExpectedDate is { } date ? $" Expected {date:MMM d}." : string.Empty;
            await notifications.ToAudienceAsync(
                Domain.Notifications.NotificationType.PurchaseOrderApproved, po.WarehouseId,
                new($"{po.Number} approved: delivery coming to {info.Warehouse}",
                    $"From {info.Supplier}.{expected} Approved by {by}.", "PurchaseOrder", po.Id),
                includeActor: false, ct);
        }

        await db.SaveChangesAsync(ct);
    }
}

public sealed class CancelPurchaseOrderHandler(IAppDbContext db, TimeProvider clock)
{
    public async Task Handle(Guid id, CancelPurchaseOrderCommand c, CancellationToken ct)
    {
        var po = await PurchaseRules.LoadAsync(db, id, ct);
        po.Cancel(c.Reason, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Receiving goods: the PO bookkeeping (received quantities, status) and the stock increase happen in ONE
/// transaction, through the same locked stock engine as manual stock-in. Either the delivery is fully recorded
/// (PO updated, GRN created, stock increased, ledger written) or nothing is.
/// </summary>
public sealed class ReceivePurchaseOrderHandler(
    IAppDbContext db, IValidator<ReceivePurchaseOrderCommand> validator, StockOperations ops,
    IDocumentNumberGenerator numbers, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<ReceiptResult> Handle(Guid id, ReceivePurchaseOrderCommand c, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(c, ct);

        var warehouseId = await db.PurchaseOrders.Where(p => p.Id == id).Select(p => (Guid?)p.WarehouseId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Purchase order", id);
        await ops.EnsureWarehouseUsableAsync(warehouseId, ct); // Inventory Staff: only their warehouses

        return await db.ExecuteInTransactionAsync(async tx =>
        {
            var po = await PurchaseRules.LoadAsync(db, id, tx);
            var now = clock.GetUtcNow();
            var quantities = c.Lines.Where(l => l.Quantity > 0).ToDictionary(l => l.ItemId, l => l.Quantity);

            po.Receive(quantities, now); // status + over-receipt rules

            var number = await numbers.NextAsync(ops.TenantId, "GRN", tx);
            var receipt = new PurchaseReceipt(ops.TenantId, number, po.Id, po.WarehouseId, c.Note, currentUser.UserId, now);

            var items = po.Items.Where(i => quantities.ContainsKey(i.Id)).ToList();
            var stocks = await ops.LockAsync(items.Select(i => (po.WarehouseId, i.ProductVariantId)), tx);

            foreach (var item in items)
            {
                var quantity = quantities[item.Id];
                var stock = stocks[(po.WarehouseId, item.ProductVariantId)];
                stock.Receive(quantity, item.UnitCost); // re-weights average cost at the PO price
                receipt.AddItem(item.Id, item.ProductVariantId, quantity, item.UnitCost);
                ops.Ledger(stock, InventoryMovementType.PurchaseReceipt, quantity, item.UnitCost, receipt.Id,
                    $"{po.Number} / {number}", c.Note, sourceType: "PurchaseOrder", sourceId: po.Id);
            }

            db.PurchaseReceipts.Add(receipt);
            // If someone else received this PO since we loaded it, its version changed → 409, nothing saved.
            await db.SaveChangesAsync(tx);
            return new ReceiptResult(receipt.Id, number, po.Status);
        }, ct);
    }
}
