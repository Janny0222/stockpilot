using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Identity;

namespace StockPilot.Application.Features.Auth.Common;

/// <summary>
/// One account per email across ALL companies (assumption A1), so this check deliberately looks past the tenant
/// filter. It returns only a yes/no, never the other account. Used by registration and by invitations.
/// </summary>
public static class EmailAvailability
{
    public static Task<bool> IsTakenAsync(IAppDbContext db, string email, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        return db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalized, ct);
    }
}
