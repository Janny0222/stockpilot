using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Platform;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

/// <summary>Super Admin only: platform.* permissions are held by no company role.</summary>
[ApiController]
[Route("api/v1/platform")]
[Produces("application/json")]
public sealed class PlatformController : ControllerBase
{
    [HttpGet("metrics")]
    [HasPermission(Permissions.Platform.MetricsView)]
    public Task<PlatformMetricsDto> Metrics([FromServices] GetPlatformMetricsHandler handler, CancellationToken ct) => handler.Handle(ct);

    [HttpGet("tenants")]
    [HasPermission(Permissions.Platform.TenantsView)]
    public Task<PagedResult<TenantSummaryDto>> List([FromQuery] ListTenantsQuery query, [FromServices] ListTenantsHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("tenants/{id:guid}")]
    [HasPermission(Permissions.Platform.TenantsView)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TenantSummaryDto> Get(Guid id, [FromServices] GetTenantHandler handler, CancellationToken ct) => handler.Handle(id, ct);

    [HttpPost("tenants/{id:guid}/suspend")]
    [HasPermission(Permissions.Platform.TenantsManage)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TenantSummaryDto> Suspend(Guid id, SuspendTenantCommand command, [FromServices] SetTenantStatusHandler handler, CancellationToken ct) =>
        handler.Handle(id, command, ct);

    [HttpGet("jobs")]
    [HasPermission(Permissions.Platform.MetricsView)]
    public Task<IReadOnlyList<JobStatusDto>> Jobs([FromServices] ListJobsHandler handler, CancellationToken ct) => handler.Handle(ct);

    /// <summary>Runs a job now (same lock and record as the scheduler). Returns when it finishes.</summary>
    [HttpPost("jobs/{name}/run")]
    [HasPermission(Permissions.Platform.TenantsManage)]
    [ProducesResponseType<Application.Common.Abstractions.JobRunOutcome>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Application.Common.Abstractions.JobRunOutcome> RunJob(string name, [FromServices] RunJobHandler handler, CancellationToken ct) =>
        handler.Handle(name, ct);

    [HttpPost("tenants/{id:guid}/activate")]
    [HasPermission(Permissions.Platform.TenantsManage)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TenantSummaryDto> Activate(Guid id, [FromServices] SetTenantStatusHandler handler, CancellationToken ct) =>
        handler.Handle(id, suspend: null, ct);
}
