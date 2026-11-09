using StockPilot.Domain.Common;

namespace StockPilot.Domain.Identity;

/// <summary>
/// Grants a warehouse-scoped user (e.g. Inventory Staff) access to one warehouse.
/// Composite key (UserId, WarehouseId). Users whose role is not warehouse-restricted ignore this table.
/// </summary>
public sealed class UserWarehouseAccess : ITenantOwned
{
    private UserWarehouseAccess() { }

    public UserWarehouseAccess(Guid tenantId, Guid userId, Guid warehouseId)
    {
        TenantId = tenantId;
        UserId = userId;
        WarehouseId = warehouseId;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid WarehouseId { get; private set; }
}
