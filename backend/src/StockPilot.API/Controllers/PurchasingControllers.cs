using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.API.Middleware;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Purchasing;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

[ApiController]
[Route("api/v1/suppliers")]
[Produces("application/json")]
public sealed class SuppliersController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Suppliers.View)]
    public Task<PagedResult<SupplierDto>> List([FromQuery] ListSuppliersQuery query, [FromServices] ListSuppliersHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Suppliers.View)]
    public Task<SupplierDto> Get(Guid id, [FromServices] GetSupplierHandler handler, CancellationToken ct) => handler.Handle(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Suppliers.Manage)]
    [ProducesResponseType<CreatedIdResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveSupplierCommand command, [FromServices] CreateSupplierHandler handler, CancellationToken ct)
    {
        var id = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreatedIdResponse(id));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Suppliers.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, SaveSupplierCommand command, [FromServices] UpdateSupplierHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.Suppliers.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, [FromServices] SetSupplierStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(Permissions.Suppliers.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restore(Guid id, [FromServices] SetSupplierStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: false, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/purchase-orders")]
[Produces("application/json")]
public sealed class PurchaseOrdersController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Purchases.View)]
    public Task<PagedResult<PurchaseOrderListItemDto>> List(
        [FromQuery] ListPurchaseOrdersQuery query, [FromServices] ListPurchaseOrdersHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Purchases.View)]
    public Task<PurchaseOrderDetailDto> Get(Guid id, [FromServices] GetPurchaseOrderHandler handler, CancellationToken ct) =>
        handler.Handle(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Purchases.Manage)]
    [ProducesResponseType<CreatedIdResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SavePurchaseOrderCommand command, [FromServices] CreatePurchaseOrderHandler handler, CancellationToken ct)
    {
        var id = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreatedIdResponse(id));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Purchases.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, SavePurchaseOrderCommand command, [FromServices] UpdatePurchaseOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.Purchases.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Submit(Guid id, [FromServices] TransitionPurchaseOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, PurchaseTransition.Submit, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.Purchases.Approve)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Approve(Guid id, [FromServices] TransitionPurchaseOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, PurchaseTransition.Approve, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Purchases.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancelPurchaseOrderCommand command, [FromServices] CancelPurchaseOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    /// <summary>Record a delivery (full or partial). Idempotent: a double-submitted delivery is applied once.</summary>
    [HttpPost("{id:guid}/receipts")]
    [HasPermission(Permissions.Purchases.Receive)]
    [Idempotent]
    [ProducesResponseType<ReceiptResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Receive(Guid id, ReceivePurchaseOrderCommand command, [FromServices] ReceivePurchaseOrderHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(id, command, ct));
}
