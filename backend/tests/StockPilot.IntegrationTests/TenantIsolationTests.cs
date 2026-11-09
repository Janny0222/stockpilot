using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Auditing;
using StockPilot.Domain.Common;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Warehousing;
using StockPilot.Infrastructure.Persistence;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

/// <summary>
/// The most important tests in the project: a company must never see or change another company's data.
/// Tested at two levels: through HTTP (what an attacker can do) and directly against the DbContext
/// guards (so a future handler bug is still caught).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(ApiFactory factory)
{
    [Fact]
    public async Task Listing_only_returns_the_callers_own_rows()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();
        await factory.AddWarehouseAsync(globex.TenantId, "GLX2");

        var page = await acme.Client.GetFromJsonAsync<Page<WarehouseItem>>("/api/v1/warehouses");

        page!.TotalCount.ShouldBe(1);
        page.Items.Single().Code.ShouldBe("MAIN");
    }

    [Fact]
    public async Task Reading_another_tenants_record_by_id_returns_404_not_403()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();
        var globexWarehouse = await factory.DefaultWarehouseIdAsync(globex.TenantId);

        var response = await acme.Client.GetAsync($"/api/v1/warehouses/{globexWarehouse}");

        // 404 (not 403) so the attacker can't even confirm the ID exists.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await globex.Client.GetAsync($"/api/v1/warehouses/{globexWarehouse}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Warehouse_scoped_staff_only_see_assigned_warehouses()
    {
        var owner = await factory.RegisterCompanyAsync();
        var main = await factory.DefaultWarehouseIdAsync(owner.TenantId);
        var other = await factory.AddWarehouseAsync(owner.TenantId, "CEB");
        var staff = await factory.CreateUserAsync(owner.TenantId, SystemRoles.InventoryStaffId, main);

        var page = await staff.Client.GetFromJsonAsync<Page<WarehouseItem>>("/api/v1/warehouses");
        page!.Items.Select(w => w.Id).ShouldBe([main]);

        (await staff.Client.GetAsync($"/api/v1/warehouses/{other}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.Client.GetFromJsonAsync<Page<WarehouseItem>>("/api/v1/warehouses"))!.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Every_tenant_owned_entity_has_a_query_filter()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unfiltered = db.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) || t.ClrType == typeof(User) || t.ClrType == typeof(AuditLog))
            .Where(t => t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        unfiltered.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_filter_hides_other_tenants_rows_even_without_a_where_clause()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();

        await using var scope = factory.CreateScopeAs(acme.TenantId, acme.UserId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantIds = await db.Warehouses.Select(w => w.TenantId).Distinct().ToListAsync();
        tenantIds.ShouldBe([acme.TenantId]);
        (await db.Users.AnyAsync(u => u.Id == globex.UserId)).ShouldBeFalse();
    }

    [Fact]
    public async Task No_tenant_context_sees_nothing()
    {
        await factory.RegisterCompanyAsync();

        await using var scope = factory.CreateScopeAs(tenantId: null);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Warehouses.CountAsync()).ShouldBe(0);
        (await db.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Inserting_a_row_for_another_tenant_is_blocked()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();

        await using var scope = factory.CreateScopeAs(acme.TenantId, acme.UserId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Warehouses.Add(new Warehouse(globex.TenantId, "EVIL", "Planted warehouse", isDefault: false));

        await Should.ThrowAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Modifying_another_tenants_row_is_blocked_even_if_it_was_loaded()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();

        await using var scope = factory.CreateScopeAs(acme.TenantId, acme.UserId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Simulates a handler bug that bypasses the read filter.
        var victim = await db.Warehouses.IgnoreQueryFilters().FirstAsync(w => w.TenantId == globex.TenantId);
        victim.SetAddress("Hacked St.", null, null);

        await Should.ThrowAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Changes_are_audited_without_secrets()
    {
        var acme = await factory.RegisterCompanyAsync();

        var logs = await factory.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == acme.TenantId).ToListAsync());

        logs.Select(l => l.EntityType).ShouldContain(nameof(StockPilot.Domain.Tenancy.Tenant));
        logs.Select(l => l.EntityType).ShouldContain(nameof(Warehouse));
        logs.ShouldContain(l => l.Action == "Auth.LoginSucceeded" && l.UserId == acme.UserId);

        var userCreated = logs.Single(l => l.EntityType == nameof(User) && l.Action == "Created");
        userCreated.Changes.ShouldNotBeNull();
        userCreated.Changes.ShouldNotContain("PasswordHash");
        userCreated.Changes.ShouldNotContain("SecurityStamp");
    }

    private sealed record Page<T>(List<T> Items, int TotalCount);

    private sealed record WarehouseItem(Guid Id, string Code);
}
