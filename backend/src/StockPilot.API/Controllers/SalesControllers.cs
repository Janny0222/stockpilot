using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.API.Middleware;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Sales;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Produces("application/json")]
public sealed class CustomersController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Customers.View)]
    public Task<PagedResult<CustomerDto>> List([FromQuery] ListCustomersQuery query, [FromServices] ListCustomersHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Customers.View)]
    public Task<CustomerDto> Get(Guid id, [FromServices] GetCustomerHandler handler, CancellationToken ct) => handler.Handle(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Customers.Manage)]
    [ProducesResponseType<CreatedIdResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCustomerCommand command, [FromServices] CreateCustomerHandler handler, CancellationToken ct)
    {
        var id = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreatedIdResponse(id));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Customers.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, SaveCustomerCommand command, [FromServices] UpdateCustomerHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.Customers.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, [FromServices] SetCustomerStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(Permissions.Customers.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restore(Guid id, [FromServices] SetCustomerStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: false, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/sales-orders")]
[Produces("application/json")]
public sealed class SalesOrdersController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sales.View)]
    public Task<PagedResult<SalesOrderListItemDto>> List(
        [FromQuery] ListSalesOrdersQuery query, [FromServices] ListSalesOrdersHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sales.View)]
    public Task<SalesOrderDetailDto> Get(Guid id, [FromServices] GetSalesOrderHandler handler, CancellationToken ct) =>
        handler.Handle(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Sales.Create)]
    [ProducesResponseType<CreatedIdResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveSalesOrderCommand command, [FromServices] CreateSalesOrderHandler handler, CancellationToken ct)
    {
        var id = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id }, new CreatedIdResponse(id));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Sales.Create)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, SaveSalesOrderCommand command, [FromServices] UpdateSalesOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    /// <summary>Confirm the order and reserve its stock. Fails with INSUFFICIENT_STOCK (nothing reserved) if any line is short.</summary>
    [HttpPost("{id:guid}/confirm")]
    [HasPermission(Permissions.Sales.Create)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Confirm(Guid id, [FromServices] ConfirmSalesOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, ct);
        return NoContent();
    }

    /// <summary>Cancel the order (or what's left of it) and release its reserved stock.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Sales.Cancel)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancelSalesOrderCommand command, [FromServices] CancelSalesOrderHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    /// <summary>Ship (fulfil) all or part of the order. Idempotent: a double-submitted shipment is applied once.</summary>
    [HttpPost("{id:guid}/shipments")]
    [HasPermission(Permissions.Sales.Fulfil)]
    [Idempotent]
    [ProducesResponseType<ShipmentResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Ship(Guid id, ShipSalesOrderCommand command, [FromServices] ShipSalesOrderHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(id, command, ct));
}
