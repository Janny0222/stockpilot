using StockPilot.Domain.Common;

namespace StockPilot.Domain.Inventory;

/// <summary>
/// Current balance of one variant in one warehouse: a fast-to-read SNAPSHOT. The source of truth is the
/// ledger (<see cref="InventoryTransaction"/>); every change here is made together with a ledger row in the
/// same database transaction, so SUM(ledger.quantity_change) always equals <see cref="QuantityOnHand"/>.
///
/// Callers must hold a row lock (SELECT … FOR UPDATE) before calling the mutating methods; see IStockLocker.
/// </summary>
public sealed class WarehouseStock : TenantEntity
{
    private WarehouseStock() { }

    public WarehouseStock(Guid tenantId, Guid warehouseId, Guid productVariantId)
        : base(tenantId)
    {
        WarehouseId = warehouseId;
        ProductVariantId = productVariantId;
    }

    public Guid WarehouseId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public decimal QuantityOnHand { get; private set; }

    /// <summary>Promised to confirmed sales orders but not yet shipped (Sales module).</summary>
    public decimal QuantityReserved { get; private set; }

    /// <summary>Weighted average cost of the units on hand. Values stock and outgoing movements (COGS).</summary>
    public decimal AverageUnitCost { get; private set; }

    public uint Version { get; private set; }

    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>
    /// Adds stock at <paramref name="unitCost"/> and re-weights the average cost:
    ///   newAvg = (onHand × avg + qty × unitCost) / (onHand + qty)
    /// e.g. 10 @ ₱100 + 30 @ ₱120 → 40 @ ₱115.
    /// </summary>
    public void Receive(decimal quantity, decimal unitCost)
    {
        EnsurePositive(quantity);
        if (unitCost < 0)
        {
            throw new DomainException("COST_NEGATIVE", "Unit cost can't be negative.");
        }

        var newQuantity = QuantityOnHand + quantity;
        AverageUnitCost = QuantityOnHand <= 0 || newQuantity <= 0
            ? unitCost
            : decimal.Round((QuantityOnHand * AverageUnitCost + quantity * unitCost) / newQuantity, 4);
        QuantityOnHand = newQuantity;
    }

    /// <summary>Removes stock. Only AVAILABLE stock (on hand minus reserved) can leave, unless the company allows negatives.</summary>
    public void Issue(decimal quantity, bool allowNegative)
    {
        EnsurePositive(quantity);
        if (!allowNegative && quantity > QuantityAvailable)
        {
            throw new InsufficientStockException(QuantityAvailable, quantity);
        }

        QuantityOnHand -= quantity;
    }

    /// <summary>
    /// Promises stock to a confirmed sales order. On-hand doesn't change; AVAILABLE drops, so nobody else can sell,
    /// transfer or issue those units. Can't reserve more than is available (unless the company allows negatives).
    /// </summary>
    public void Reserve(decimal quantity, bool allowNegative, string? sku = null)
    {
        EnsurePositive(quantity);
        if (!allowNegative && quantity > QuantityAvailable)
        {
            throw new InsufficientStockException(QuantityAvailable, quantity, sku);
        }

        QuantityReserved += quantity;
    }

    /// <summary>Gives reserved units back to available (order cancelled). Never below zero.</summary>
    public void Release(decimal quantity)
    {
        EnsurePositive(quantity);
        QuantityReserved = Math.Max(0, QuantityReserved - quantity);
    }

    /// <summary>Ships reserved units: they leave on-hand AND the reservation in one step.</summary>
    public void ShipReserved(decimal quantity)
    {
        EnsurePositive(quantity);
        if (quantity > QuantityReserved)
        {
            throw new DomainException("NOT_RESERVED", $"Only {QuantityReserved:0.###} reserved, but {quantity:0.###} to ship.");
        }

        QuantityReserved -= quantity;
        QuantityOnHand -= quantity;
    }

    /// <summary>Sets on-hand to a counted quantity (stocktake). Returns the signed difference that was applied.</summary>
    public decimal AdjustTo(decimal countedQuantity)
    {
        if (countedQuantity < 0)
        {
            throw new DomainException("QUANTITY_NEGATIVE", "A counted quantity can't be negative.");
        }

        if (countedQuantity < QuantityReserved)
        {
            throw new DomainException("BELOW_RESERVED",
                $"{QuantityReserved:0.###} units are reserved for open orders, so the count can't be lower than that.");
        }

        var delta = countedQuantity - QuantityOnHand;
        QuantityOnHand = countedQuantity;
        return delta;
    }

    /// <summary>
    /// Stock that appears without a purchase (e.g. "found" in a count) has no cost history. Give it the catalogue
    /// cost so inventory valuation isn't ₱0. Never overrides an existing average.
    /// </summary>
    public void InitializeCostIfMissing(decimal unitCost)
    {
        if (AverageUnitCost == 0 && unitCost > 0)
        {
            AverageUnitCost = decimal.Round(unitCost, 4);
        }
    }

    private static void EnsurePositive(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("QUANTITY_INVALID", "Quantity must be greater than zero.");
        }
    }
}

/// <summary>422 with the numbers the UI needs to explain it ("only 3 available").</summary>
public sealed class InsufficientStockException(decimal available, decimal requested, string? sku = null)
    : DomainException("INSUFFICIENT_STOCK", $"{(sku is null ? "" : sku + ": ")}Only {available:0.###} available, but {requested:0.###} requested.")
{
    public decimal Available { get; } = available;
    public decimal Requested { get; } = requested;
}
