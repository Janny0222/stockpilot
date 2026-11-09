using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

[ApiController]
[Route("api/v1/warehouses")]
[Produces("application/json")]
public sealed class WarehousesController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Warehouses.View)]
    [ProducesResponseType<PagedResult<WarehouseDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<WarehouseDto>> List(
        [FromQuery] ListWarehousesQuery query, [FromServices] ListWarehousesHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Warehouses.View)]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<WarehouseDto> Get(Guid id, [FromServices] GetWarehouseHandler handler, CancellationToken ct) =>
        handler.Handle(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Warehouses.Manage)]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveWarehouseCommand command, [FromServices] CreateWarehouseHandler handler, CancellationToken ct)
    {
        var warehouse = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id = warehouse.Id }, warehouse);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Warehouses.Manage)]
    public Task<WarehouseDto> Update(Guid id, SaveWarehouseCommand command, [FromServices] UpdateWarehouseHandler handler, CancellationToken ct) =>
        handler.Handle(id, command, ct);

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.Warehouses.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, [FromServices] SetWarehouseStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(Permissions.Warehouses.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restore(Guid id, [FromServices] SetWarehouseStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: false, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/default")]
    [HasPermission(Permissions.Warehouses.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MakeDefault(Guid id, [FromServices] SetDefaultWarehouseHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, ct);
        return NoContent();
    }
}
