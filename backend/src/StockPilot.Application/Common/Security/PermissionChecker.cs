using StockPilot.Application.Common.Abstractions;

namespace StockPilot.Application.Common.Security;

/// <summary>
/// For decisions INSIDE a use case (not just "may call this endpoint"), e.g. "include cost prices in
/// the response only if the caller has inventory.view_cost". Endpoint-level checks use [HasPermission].
/// </summary>
public sealed class PermissionChecker(ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<bool> HasAsync(string permission, CancellationToken ct) =>
        currentUser.RoleId is { } roleId && (await permissions.GetPermissionsAsync(roleId, ct)).Contains(permission);
}
