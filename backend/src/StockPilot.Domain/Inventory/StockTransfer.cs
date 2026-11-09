using StockPilot.Domain.Common;

namespace StockPilot.Domain.Inventory;

public enum StockTransferStatus
{
    Completed = 1,
    Cancelled = 2,
}

/// <summary>
/// Moving stock between two warehouses of the same company. Created and completed in one atomic step:
/// both legs (TransferOut at the source, TransferIn at the destination) are written in the same database
/// transaction as this document, so stock is never "half moved". A two-step in-transit flow is a planned
/// advanced feature.
/// </summary>
public sealed class StockTransfer : TenantEntity
{
    public const int NoteMaxLength = 300;

    private readonly List<StockTransferItem> _items = [];

    private StockTransfer() { }

    public StockTransfer(Guid tenantId, string number, Guid sourceWarehouseId, Guid destinationWarehouseId, string? note, Guid? completedBy, DateTimeOffset completedAt)
        : base(tenantId)
    {
        if (sourceWarehouseId == destinationWarehouseId)
        {
            throw new DomainException("TRANSFER_SAME_WAREHOUSE", "Choose two different warehouses.");
        }

        Number = number;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = StockTransferStatus.Completed;
        CompletedBy = completedBy;
        CompletedAt = completedAt;
    }

    /// <summary>Human-readable, sequential per company: TR-000042.</summary>
    public string Number { get; private set; } = string.Empty;
    public Guid SourceWarehouseId { get; private set; }
    public Guid DestinationWarehouseId { get; private set; }
    public StockTransferStatus Status { get; private set; }
    public string? Note { get; private set; }
    public Guid? CompletedBy { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public IReadOnlyCollection<StockTransferItem> Items => _items;

    public void AddItem(Guid productVariantId, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0)
        {
            throw new DomainException("QUANTITY_INVALID", "Quantity must be greater than zero.");
        }

        if (_items.Any(i => i.ProductVariantId == productVariantId))
        {
            throw new DomainException("DUPLICATE_LINE", "Each product variant can appear only once per transfer.");
        }

        _items.Add(new StockTransferItem(TenantId, Id, productVariantId, quantity, unitCost));
    }
}

public sealed class StockTransferItem : TenantEntity
{
    private StockTransferItem() { }

    internal StockTransferItem(Guid tenantId, Guid stockTransferId, Guid productVariantId, decimal quantity, decimal unitCost)
        : base(tenantId)
    {
        StockTransferId = stockTransferId;
        ProductVariantId = productVariantId;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public Guid StockTransferId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Source warehouse's average cost at the moment of transfer: the destination receives at this cost.</summary>
    public decimal UnitCost { get; private set; }
}
