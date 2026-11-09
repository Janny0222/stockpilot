using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Warehouses;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

/// <summary>
/// Read-only for now. It exists in Phase 2 as the first tenant-owned resource, so isolation and IDOR
/// protection can be proven end-to-end. Create/update/archive arrive with the Warehouses module.
/// </summary>
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
}
