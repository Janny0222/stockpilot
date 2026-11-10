using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Settings;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

[ApiController]
[Route("api/v1/settings/company")]
[Produces("application/json")]
public sealed class CompanySettingsController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Company.Manage)]
    public Task<CompanySettingsDto> Get([FromServices] GetCompanySettingsHandler handler, CancellationToken ct) => handler.Handle(ct);

    [HttpPut]
    [HasPermission(Permissions.Company.Manage)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CompanySettingsDto> Update(UpdateCompanySettingsCommand command, [FromServices] UpdateCompanySettingsHandler handler, CancellationToken ct) =>
        handler.Handle(command, ct);
}

[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class UsersController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.Manage)]
    public Task<PagedResult<UserDto>> List([FromQuery] ListUsersQuery query, [FromServices] ListUsersHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<UserDto> Get(Guid id, [FromServices] GetUserHandler handler, CancellationToken ct) => handler.Handle(id, ct);

    /// <summary>Invites someone by email. They choose their own password from the link.</summary>
    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invite(InviteUserCommand command, [FromServices] InviteUserHandler handler, CancellationToken ct)
    {
        var user = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    public Task<UserDto> Update(Guid id, UpdateUserCommand command, [FromServices] UpdateUserHandler handler, CancellationToken ct) =>
        handler.Handle(id, command, ct);

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, [FromServices] SetUserStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, active: false, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, [FromServices] SetUserStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, active: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendInvitation(Guid id, [FromServices] ResendInvitationHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/roles")]
[Produces("application/json")]
public sealed class RolesController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.Manage)]
    public Task<RolesOverviewDto> List([FromServices] ListRolesHandler handler, CancellationToken ct) => handler.Handle(ct);
}

[ApiController]
[Route("api/v1/audit-logs")]
[Produces("application/json")]
public sealed class AuditLogsController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Audit.View)]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] ListAuditLogsQuery query, [FromServices] ListAuditLogsHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("filters")]
    [HasPermission(Permissions.Audit.View)]
    public Task<AuditFiltersDto> Filters([FromServices] GetAuditFiltersHandler handler, CancellationToken ct) => handler.Handle(ct);
}
