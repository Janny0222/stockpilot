using StockPilot.Domain.Common;

namespace StockPilot.Domain.Purchasing;

public enum PurchaseOrderStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    PartiallyReceived = 4,
    Received = 5,
    Cancelled = 6,
}

/// <summary>
/// An order to a supplier, delivered into one warehouse. A small state machine: every transition method checks
/// the current status and throws a DomainException (→ 422) when the move isn't allowed, so the rules live in ONE
/// place instead of being re-checked in every handler and screen.
///
///   Draft → Submitted → Approved → PartiallyReceived → Received
///   Draft / Submitted / Approved → Cancelled   (only while nothing has been received)
/// </summary>
public sealed class PurchaseOrder : TenantEntity
{
    public const int NotesMaxLength = 500;
    public const int MaxItems = 100;

    private readonly List<PurchaseOrderItem> _items = [];

    private PurchaseOrder() { }

    public PurchaseOrder(Guid tenantId, string number, Guid supplierId, Guid warehouseId, DateOnly orderDate, DateOnly? expectedDate, string? notes)
        : base(tenantId)
    {
        Number = number;
        Status = PurchaseOrderStatus.Draft;
        SetHeader(supplierId, warehouseId, orderDate, expectedDate, notes);
    }

    /// <summary>PO-000123, sequential per company.</summary>
    public string Number { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public DateOnly? ExpectedDate { get; private set; }
    public string? Notes { get; private set; }
    public decimal Total { get; private set; }

    public Guid? SubmittedBy { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>Touched on every receipt so the row's concurrency token changes (see Receive).</summary>
    public DateTimeOffset? LastReceivedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<PurchaseOrderItem> Items => _items;

    public bool IsEditable => Status == PurchaseOrderStatus.Draft;
    public bool HasReceipts => _items.Any(i => i.QuantityReceived > 0);

    // ---------------- Draft editing ----------------

    public void SetHeader(Guid supplierId, Guid warehouseId, DateOnly orderDate, DateOnly? expectedDate, string? notes)
    {
        EnsureStatus(PurchaseOrderStatus.Draft, "Only draft orders can be edited.");

        if (expectedDate is { } expected && expected < orderDate)
        {
            throw new DomainException("EXPECTED_BEFORE_ORDER", "The expected delivery date can't be before the order date.");
        }

        SupplierId = supplierId;
        WarehouseId = warehouseId;
        OrderDate = orderDate;
        ExpectedDate = expectedDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>Replaces all lines (the edit screen sends the whole list). Draft only.</summary>
    public void ReplaceItems(IEnumerable<(Guid VariantId, decimal Quantity, decimal UnitCost)> lines)
    {
        EnsureStatus(PurchaseOrderStatus.Draft, "Only draft orders can be edited.");

        var list = lines.ToList();
        if (list.Count > MaxItems)
        {
            throw new DomainException("TOO_MANY_LINES", $"An order can have at most {MaxItems} lines.");
        }

        if (list.Select(l => l.VariantId).Distinct().Count() != list.Count)
        {
            throw new DomainException("DUPLICATE_LINE", "Each item can appear only once per order.");
        }

        _items.Clear();
        foreach (var (variantId, quantity, unitCost) in list)
        {
            _items.Add(new PurchaseOrderItem(TenantId, Id, variantId, quantity, unitCost));
        }

        Total = _items.Sum(i => i.LineTotal);
    }

    // ---------------- Transitions ----------------

    public void Submit(Guid? by, DateTimeOffset now)
    {
        EnsureStatus(PurchaseOrderStatus.Draft, "Only draft orders can be submitted.");
        if (_items.Count == 0)
        {
            throw new DomainException("ORDER_EMPTY", "Add at least one item before submitting.");
        }

        Status = PurchaseOrderStatus.Submitted;
        SubmittedBy = by;
        SubmittedAt = now;
    }

    public void Approve(Guid? by, DateTimeOffset now)
    {
        EnsureStatus(PurchaseOrderStatus.Submitted, "Only submitted orders can be approved.");
        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = by;
        ApprovedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted or PurchaseOrderStatus.Approved))
        {
            throw new DomainException("INVALID_STATUS", $"A {Describe(Status)} order can't be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("REASON_REQUIRED", "Give a reason for cancelling.");
        }

        Status = PurchaseOrderStatus.Cancelled;
        CancelledAt = now;
        CancelReason = reason.Trim();
    }

    /// <summary>
    /// Records delivered quantities against the order's lines and moves the status forward. Never more than
    /// was ordered per line. Also stamps <see cref="LastReceivedAt"/> so the PO row itself changes: two people
    /// receiving the same order at once then collide on its concurrency token (one gets 409), instead of both
    /// passing the "≤ remaining" check and over-receiving.
    /// </summary>
    public void Receive(IReadOnlyDictionary<Guid, decimal> quantitiesByItemId, DateTimeOffset now)
    {
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
        {
            throw new DomainException("INVALID_STATUS", $"A {Describe(Status)} order can't be received. Approve it first.");
        }

        if (quantitiesByItemId.Count == 0 || quantitiesByItemId.Values.All(q => q == 0))
        {
            throw new DomainException("NOTHING_TO_RECEIVE", "Enter a quantity for at least one item.");
        }

        foreach (var (itemId, quantity) in quantitiesByItemId)
        {
            var item = _items.SingleOrDefault(i => i.Id == itemId)
                ?? throw new DomainException("LINE_NOT_FOUND", "That item isn't on this order.");
            item.Receive(quantity);
        }

        Status = _items.All(i => i.IsFullyReceived) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        LastReceivedAt = now;
    }

    private void EnsureStatus(PurchaseOrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainException("INVALID_STATUS", message);
        }
    }

    private static string Describe(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.PartiallyReceived => "partially received",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public sealed class PurchaseOrderItem : TenantEntity
{
    private PurchaseOrderItem() { }

    internal PurchaseOrderItem(Guid tenantId, Guid purchaseOrderId, Guid variantId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
        if (quantity <= 0)
        {
            throw new DomainException("QUANTITY_INVALID", "Quantity must be greater than zero.");
        }

        if (unitCost < 0)
        {
            throw new DomainException("COST_NEGATIVE", "Unit cost can't be negative.");
        }

        PurchaseOrderId = purchaseOrderId;
        ProductVariantId = variantId;
        QuantityOrdered = quantity;
        UnitCost = decimal.Round(unitCost, 4);
        LineTotal = decimal.Round(quantity * unitCost, 2);
    }

    public Guid PurchaseOrderId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public decimal QuantityOrdered { get; private set; }
    public decimal QuantityReceived { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal LineTotal { get; private set; }

    public decimal QuantityRemaining => QuantityOrdered - QuantityReceived;
    public bool IsFullyReceived => QuantityReceived >= QuantityOrdered;

    internal void Receive(decimal quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException("QUANTITY_INVALID", "Received quantity can't be negative.");
        }

        if (quantity > QuantityRemaining)
        {
            throw new DomainException("OVER_RECEIPT",
                $"Only {QuantityRemaining:0.###} still to receive on this line, but {quantity:0.###} entered.");
        }

        QuantityReceived += quantity;
    }
}

/// <summary>Goods Received Note: one delivery against a PO. Partial deliveries = several receipts.</summary>
public sealed class PurchaseReceipt : TenantEntity
{
    private readonly List<PurchaseReceiptItem> _items = [];

    private PurchaseReceipt() { }

    public PurchaseReceipt(Guid tenantId, string number, Guid purchaseOrderId, Guid warehouseId, string? note, Guid? receivedBy, DateTimeOffset receivedAt)
        : base(tenantId)
    {
        Number = number;
        PurchaseOrderId = purchaseOrderId;
        WarehouseId = warehouseId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ReceivedBy = receivedBy;
        ReceivedAt = receivedAt;
    }

    /// <summary>GRN-000045.</summary>
    public string Number { get; private set; } = string.Empty;
    public Guid PurchaseOrderId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string? Note { get; private set; }
    public Guid? ReceivedBy { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    public IReadOnlyCollection<PurchaseReceiptItem> Items => _items;

    public void AddItem(Guid purchaseOrderItemId, Guid variantId, decimal quantity, decimal unitCost) =>
        _items.Add(new PurchaseReceiptItem(TenantId, Id, purchaseOrderItemId, variantId, quantity, unitCost));
}

public sealed class PurchaseReceiptItem : TenantEntity
{
    private PurchaseReceiptItem() { }

    internal PurchaseReceiptItem(Guid tenantId, Guid receiptId, Guid purchaseOrderItemId, Guid variantId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
        PurchaseReceiptId = receiptId;
        PurchaseOrderItemId = purchaseOrderItemId;
        ProductVariantId = variantId;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public Guid PurchaseReceiptId { get; private set; }
    public Guid PurchaseOrderItemId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
}
