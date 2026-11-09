using StockPilot.Domain.Common;

namespace StockPilot.Domain.Sales;

public enum SalesOrderStatus
{
    Draft = 1,
    Confirmed = 2,
    PartiallyFulfilled = 3,
    Fulfilled = 4,
    Cancelled = 5,
}

/// <summary>
/// An order from a customer, shipped from one warehouse.
///
///   Draft ──confirm (reserves stock)──► Confirmed ──ship──► PartiallyFulfilled ──ship──► Fulfilled
///   Draft / Confirmed / PartiallyFulfilled ──cancel (releases what's still reserved)──► Cancelled
///
/// While an order is Confirmed or PartiallyFulfilled, every line's REMAINING quantity is reserved in the warehouse.
/// The order only does the bookkeeping. The handler applies the matching Reserve / ShipReserved / Release calls to
/// the locked stock rows in the same transaction, using the quantities these methods return.
/// </summary>
public sealed class SalesOrder : TenantEntity
{
    public const int NotesMaxLength = 500;
    public const int MaxItems = 100;

    private readonly List<SalesOrderItem> _items = [];

    private SalesOrder() { }

    public SalesOrder(Guid tenantId, string number, Guid customerId, Guid warehouseId, DateOnly orderDate, string? notes)
        : base(tenantId)
    {
        Number = number;
        Status = SalesOrderStatus.Draft;
        SetHeader(customerId, warehouseId, orderDate, notes);
    }

    /// <summary>SO-000123, sequential per company.</summary>
    public string Number { get; private set; } = string.Empty;
    public Guid CustomerId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public string? Notes { get; private set; }
    public decimal Total { get; private set; }

    public Guid? ConfirmedBy { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>Touched on every shipment so the row's concurrency token changes: two simultaneous shipments of
    /// the same order collide (one gets 409) instead of both shipping the same remaining quantity.</summary>
    public DateTimeOffset? LastShippedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyCollection<SalesOrderItem> Items => _items;

    /// <summary>True while stock is held for this order.</summary>
    public bool HoldsReservation => Status is SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyFulfilled;

    // ---------------- Draft editing ----------------

    public void SetHeader(Guid customerId, Guid warehouseId, DateOnly orderDate, string? notes)
    {
        EnsureDraft();
        CustomerId = customerId;
        WarehouseId = warehouseId;
        OrderDate = orderDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void ReplaceItems(IEnumerable<(Guid VariantId, decimal Quantity, decimal UnitPrice)> lines)
    {
        EnsureDraft();

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
        foreach (var (variantId, quantity, unitPrice) in list)
        {
            _items.Add(new SalesOrderItem(TenantId, Id, variantId, quantity, unitPrice));
        }

        Total = _items.Sum(i => i.LineTotal);
    }

    // ---------------- Transitions ----------------

    /// <summary>Draft → Confirmed. Returns what must now be reserved, per variant.</summary>
    public IReadOnlyList<(Guid VariantId, decimal Quantity)> Confirm(Guid? by, DateTimeOffset now)
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new DomainException("INVALID_STATUS", "Only draft orders can be confirmed.");
        }

        if (_items.Count == 0)
        {
            throw new DomainException("ORDER_EMPTY", "Add at least one item before confirming.");
        }

        Status = SalesOrderStatus.Confirmed;
        ConfirmedBy = by;
        ConfirmedAt = now;
        return _items.Select(i => (i.ProductVariantId, i.QuantityOrdered)).ToList();
    }

    /// <summary>Records shipped quantities. Never more than is left on a line.</summary>
    public void Ship(IReadOnlyDictionary<Guid, decimal> quantitiesByItemId, DateTimeOffset now)
    {
        if (!HoldsReservation)
        {
            throw new DomainException("INVALID_STATUS", $"A {Describe(Status)} order can't be shipped. Confirm it first.");
        }

        if (quantitiesByItemId.Count == 0 || quantitiesByItemId.Values.All(q => q == 0))
        {
            throw new DomainException("NOTHING_TO_SHIP", "Enter a quantity for at least one item.");
        }

        foreach (var (itemId, quantity) in quantitiesByItemId)
        {
            var item = _items.SingleOrDefault(i => i.Id == itemId)
                ?? throw new DomainException("LINE_NOT_FOUND", "That item isn't on this order.");
            item.Ship(quantity);
        }

        Status = _items.All(i => i.IsFullyShipped) ? SalesOrderStatus.Fulfilled : SalesOrderStatus.PartiallyFulfilled;
        LastShippedAt = now;
    }

    /// <summary>
    /// Cancels the order (or, if partly shipped, everything not yet shipped). Returns the reservations to release:
    /// empty for a draft, which never reserved anything.
    /// </summary>
    public IReadOnlyList<(Guid VariantId, decimal Quantity)> Cancel(string reason, DateTimeOffset now)
    {
        if (Status is SalesOrderStatus.Fulfilled or SalesOrderStatus.Cancelled)
        {
            throw new DomainException("INVALID_STATUS", $"A {Describe(Status)} order can't be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("REASON_REQUIRED", "Give a reason for cancelling.");
        }

        var release = HoldsReservation
            ? _items.Where(i => i.QuantityRemaining > 0).Select(i => (i.ProductVariantId, i.QuantityRemaining)).ToList()
            : [];

        Status = SalesOrderStatus.Cancelled;
        CancelledAt = now;
        CancelReason = reason.Trim();
        return release;
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new DomainException("INVALID_STATUS", "Only draft orders can be edited.");
        }
    }

    private static string Describe(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.PartiallyFulfilled => "partially fulfilled",
        _ => status.ToString().ToLowerInvariant(),
    };
}

public sealed class SalesOrderItem : TenantEntity
{
    private SalesOrderItem() { }

    internal SalesOrderItem(Guid tenantId, Guid salesOrderId, Guid variantId, decimal quantity, decimal unitPrice)
        : base(tenantId)
    {
        if (quantity <= 0)
        {
            throw new DomainException("QUANTITY_INVALID", "Quantity must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("PRICE_NEGATIVE", "Unit price can't be negative.");
        }

        SalesOrderId = salesOrderId;
        ProductVariantId = variantId;
        QuantityOrdered = quantity;
        UnitPrice = decimal.Round(unitPrice, 4);
        LineTotal = decimal.Round(quantity * unitPrice, 2);
    }

    public Guid SalesOrderId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public decimal QuantityOrdered { get; private set; }
    public decimal QuantityShipped { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    public decimal QuantityRemaining => QuantityOrdered - QuantityShipped;
    public bool IsFullyShipped => QuantityShipped >= QuantityOrdered;

    internal void Ship(decimal quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException("QUANTITY_INVALID", "Shipped quantity can't be negative.");
        }

        if (quantity > QuantityRemaining)
        {
            throw new DomainException("OVER_SHIPMENT",
                $"Only {QuantityRemaining:0.###} still to ship on this line, but {quantity:0.###} entered.");
        }

        QuantityShipped += quantity;
    }
}

/// <summary>Delivery receipt (DR): one shipment against a sales order. Partial deliveries = several shipments.</summary>
public sealed class SalesShipment : TenantEntity
{
    private readonly List<SalesShipmentItem> _items = [];

    private SalesShipment() { }

    public SalesShipment(Guid tenantId, string number, Guid salesOrderId, Guid warehouseId, string? note, Guid? shippedBy, DateTimeOffset shippedAt)
        : base(tenantId)
    {
        Number = number;
        SalesOrderId = salesOrderId;
        WarehouseId = warehouseId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ShippedBy = shippedBy;
        ShippedAt = shippedAt;
    }

    /// <summary>DR-000045.</summary>
    public string Number { get; private set; } = string.Empty;
    public Guid SalesOrderId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public string? Note { get; private set; }
    public Guid? ShippedBy { get; private set; }
    public DateTimeOffset ShippedAt { get; private set; }

    public IReadOnlyCollection<SalesShipmentItem> Items => _items;

    /// <summary><paramref name="unitCost"/> is the warehouse's average cost at shipping time: the cost of goods sold.</summary>
    public void AddItem(Guid salesOrderItemId, Guid variantId, decimal quantity, decimal unitPrice, decimal unitCost) =>
        _items.Add(new SalesShipmentItem(TenantId, Id, salesOrderItemId, variantId, quantity, unitPrice, unitCost));
}

public sealed class SalesShipmentItem : TenantEntity
{
    private SalesShipmentItem() { }

    internal SalesShipmentItem(Guid tenantId, Guid shipmentId, Guid salesOrderItemId, Guid variantId, decimal quantity, decimal unitPrice, decimal unitCost)
        : base(tenantId)
    {
        SalesShipmentId = shipmentId;
        SalesOrderItemId = salesOrderItemId;
        ProductVariantId = variantId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        UnitCost = unitCost;
    }

    public Guid SalesShipmentId { get; private set; }
    public Guid SalesOrderItemId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal UnitCost { get; private set; }
}
