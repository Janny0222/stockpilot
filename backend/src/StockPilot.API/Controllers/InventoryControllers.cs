using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.API.Middleware;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Inventory;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

/// <summary>
/// Stock levels, the movement ledger and manual stock operations. Every write requires an Idempotency-Key
/// and runs in one database transaction with row locks (see StockOperations / IStockLocker).
/// </summary>
[ApiController]
[Route("api/v1/inventory")]
[Produces("application/json")]
public sealed class InventoryController : ControllerBase
{
    [HttpGet("stock")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<StockLevelDto>> Stock([FromQuery] ListStockQuery query, [FromServices] ListStockHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("movements")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<MovementDto>> Movements([FromQuery] ListMovementsQuery query, [FromServices] ListMovementsHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    /// <summary>Item picker for stock forms: variants with their stock in <paramref name="warehouseId"/>.</summary>
    [HttpGet("variants")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<IReadOnlyList<VariantOptionDto>> Variants(
        [FromQuery] string? search, [FromQuery] Guid? warehouseId, [FromServices] SearchStockVariantsHandler handler, CancellationToken ct) =>
        handler.Handle(search, warehouseId, ct);

    [HttpPost("stock-in")]
    [HasPermission(Permissions.Inventory.StockIn)]
    [Idempotent]
    [ProducesResponseType<StockOperationResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> StockIn(RecordStockCommand command, [FromServices] StockInHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(command, ct));

    [HttpPost("stock-out")]
    [HasPermission(Permissions.Inventory.StockOut)]
    [Idempotent]
    [ProducesResponseType<StockOperationResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> StockOut(RecordStockCommand command, [FromServices] StockOutHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(command, ct));

    [HttpPost("adjustments")]
    [HasPermission(Permissions.Inventory.Adjust)]
    [Idempotent]
    [ProducesResponseType<StockOperationResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Adjust(AdjustStockCommand command, [FromServices] AdjustStockHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(command, ct));
}

[ApiController]
[Route("api/v1/transfers")]
[Produces("application/json")]
public sealed class TransfersController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Inventory.View)]
    public Task<PagedResult<TransferListItemDto>> List([FromQuery] PageRequest query, [FromServices] ListTransfersHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inventory.View)]
    public Task<TransferDetailDto> Get(Guid id, [FromServices] GetTransferHandler handler, CancellationToken ct) =>
        handler.Handle(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Inventory.Transfer)]
    [Idempotent]
    [ProducesResponseType<StockOperationResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(TransferStockCommand command, [FromServices] TransferStockHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(command, ct));
}
