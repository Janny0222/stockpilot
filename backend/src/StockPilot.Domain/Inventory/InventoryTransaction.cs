using StockPilot.Domain.Common;

namespace StockPilot.Domain.Inventory;

public enum InventoryMovementType
{
    StockIn = 1,
    StockOut = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
    TransferOut = 5,
    TransferIn = 6,
    PurchaseReceipt = 7,
    SalesFulfilment = 8,
}

public enum AdjustmentReason
{
    CountCorrection = 1,
    Damaged = 2,
    Lost = 3,
    Found = 4,
    Expired = 5,
    Other = 6,
}

/// <summary>
/// One immutable line of the inventory ledger. Never updated, never deleted: a mistake is corrected by a
/// new, compensating movement. This is how accounting ledgers work too: the history IS the audit trail.
/// </summary>
public sealed class InventoryTransaction : Entity, ITenantOwned
{
    public const int ReferenceMaxLength = 60;
    public const int NoteMaxLength = 300;

    private InventoryTransaction() { }

    public InventoryTransaction(
        Guid tenantId,
        Guid warehouseId,
        Guid productVariantId,
        InventoryMovementType type,
        decimal quantityChange,
        decimal quantityAfter,
        decimal unitCost,
        Guid correlationId,
        string? reference,
        string? note,
        AdjustmentReason? reason,
        string? sourceType,
        Guid? sourceId,
        Guid? performedBy,
        DateTimeOffset occurredAt)
    {
        TenantId = tenantId;
        WarehouseId = warehouseId;
        ProductVariantId = productVariantId;
        Type = type;
        QuantityChange = quantityChange;
        QuantityAfter = quantityAfter;
        UnitCost = unitCost;
        CorrelationId = correlationId;
        Reference = reference;
        Note = note;
        Reason = reason;
        SourceType = sourceType;
        SourceId = sourceId;
        PerformedBy = performedBy;
        OccurredAt = occurredAt;
    }

    public Guid TenantId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public InventoryMovementType Type { get; private set; }

    /// <summary>Signed: +10 for stock coming in, −3 for stock going out.</summary>
    public decimal QuantityChange { get; private set; }

    /// <summary>Balance in that warehouse right after this movement (handy for history, and for auditors).</summary>
    public decimal QuantityAfter { get; private set; }

    public decimal UnitCost { get; private set; }

    /// <summary>Groups the lines of one operation (a 5-line stock-in, both legs of a transfer).</summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>User-entered reference, e.g. a delivery receipt number.</summary>
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    public AdjustmentReason? Reason { get; private set; }

    /// <summary>Document that caused it, e.g. ("StockTransfer", id). Null for manual movements.</summary>
    public string? SourceType { get; private set; }
    public Guid? SourceId { get; private set; }

    public Guid? PerformedBy { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
