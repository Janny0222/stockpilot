using Microsoft.AspNetCore.Authorization;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Infrastructure.Identity;

namespace StockPilot.API.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Usage: <c>[HasPermission(Permissions.Warehouses.View)]</c>.
/// Implements IAuthorizationRequirementData (ASP.NET Core 8+), so no named policy has to be
/// registered per permission; the attribute carries its own requirement.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(Permission)];
}

/// <summary>Resolves the caller's role (from the "rid" claim) to its permission set (cached) and checks membership.</summary>
internal sealed class PermissionAuthorizationHandler(IPermissionService permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst(StockPilotClaims.RoleId)?.Value, out var roleId))
        {
            return;
        }

        var granted = await permissions.GetPermissionsAsync(roleId, CancellationToken.None);
        if (granted.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
