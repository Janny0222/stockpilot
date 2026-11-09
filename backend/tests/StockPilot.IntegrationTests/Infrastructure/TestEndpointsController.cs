using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Domain.Identity;

namespace StockPilot.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only endpoints, registered via an ApplicationPart in <see cref="ApiFactory"/>.
/// They let us prove the permission pipeline for permissions whose real endpoints don't exist yet.
/// </summary>
[ApiController]
[Route("api/test")]
public sealed class TestEndpointsController : ControllerBase
{
    [HttpGet("adjust-stock")]
    [HasPermission(Permissions.Inventory.Adjust)]
    public IActionResult RequiresAdjust() => Ok(new { ok = true });

    [HttpGet("platform")]
    [HasPermission(Permissions.Platform.TenantsView)]
    public IActionResult RequiresPlatform() => Ok(new { ok = true });
}
