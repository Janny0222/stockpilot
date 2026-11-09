using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Identity;
using StockPilot.Domain.Tenancy;
using StockPilot.Domain.Warehousing;

namespace StockPilot.Infrastructure.Persistence.Seed;

/// <summary>
/// Startup data tasks, each opt-in via configuration:
///   Database:MigrateOnStartup    apply pending migrations (local dev; production migrates from CI)
///   Platform:SuperAdmin:*        create the platform admin if missing (credentials from env/secrets)
///   Seed:DemoData                create a realistic demo company (dev / public demo only)
/// Reference data (roles, permissions) is NOT here: it's in migrations via HasData.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = sp.GetRequiredService<IConfiguration>();
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (config.GetValue<bool>("Database:MigrateOnStartup"))
        {
            logger.LogInformation("Applying database migrations…");
            await db.Database.MigrateAsync(ct);
        }

        await SeedSuperAdminAsync(db, sp.GetRequiredService<IPasswordHasher>(), config, logger, ct);

        if (config.GetValue<bool>("Seed:DemoData"))
        {
            await SeedDemoTenantAsync(db, sp.GetRequiredService<IPasswordHasher>(), config, logger, ct);
        }
    }

    private static async Task SeedSuperAdminAsync(
        AppDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var email = config["Platform:SuperAdmin:Email"];
        var password = config["Platform:SuperAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var normalized = User.NormalizeEmail(email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalized, ct))
        {
            return;
        }

        var admin = User.CreatePlatformAdmin(normalized, "Platform", "Admin");
        admin.SetPasswordHash(hasher.Hash(password));
        admin.ConfirmEmail();
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded platform super admin {Email}", normalized);
    }

    private static async Task SeedDemoTenantAsync(
        AppDbContext db, IPasswordHasher hasher, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        const string slug = "northwind-supply";
        var existingId = await db.Tenants.Where(t => t.Slug == slug).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct);
        if (existingId is { } tenantId)
        {
            // Company already exists: top up data added by later modules (each step is idempotent).
            await SeedModuleDataAsync(db, tenantId, logger, ct);
            return;
        }

        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:DemoData is on but Seed:DemoPassword is empty; skipping demo seed.");
            return;
        }

        var tenant = Tenant.Register("Northwind Supply Co.", slug, "PHP", "Asia/Manila");
        var main = new Warehouse(tenant.Id, "MNL", "Manila Central Warehouse", isDefault: true);
        main.SetAddress("1200 Pioneer St.", "Mandaluyong", "Philippines");
        var cebu = new Warehouse(tenant.Id, "CEB", "Cebu Distribution Hub", isDefault: false);
        cebu.SetAddress("45 Mandaue Reclamation Area", "Cebu City", "Philippines");
        var davao = new Warehouse(tenant.Id, "DVO", "Davao Satellite Store", isDefault: false);
        davao.SetAddress("88 J.P. Laurel Ave.", "Davao City", "Philippines");

        var hash = hasher.Hash(password);
        User NewUser(Guid roleId, string email, string first, string last)
        {
            var user = User.CreateTenantUser(tenant.Id, roleId, email, first, last);
            user.SetPasswordHash(hash);
            user.ConfirmEmail();
            return user;
        }

        var inventoryStaff = NewUser(SystemRoles.InventoryStaffId, "inventory@demo.stockpilot.app", "Paolo", "Reyes");

        db.Tenants.Add(tenant);
        db.Warehouses.AddRange(main, cebu, davao);
        db.Users.AddRange(
            NewUser(SystemRoles.OwnerId, "owner@demo.stockpilot.app", "Maria", "Santos"),
            NewUser(SystemRoles.ManagerId, "manager@demo.stockpilot.app", "Jose", "Cruz"),
            inventoryStaff,
            NewUser(SystemRoles.SalesStaffId, "sales@demo.stockpilot.app", "Ana", "Garcia"));
        db.UserWarehouseAccess.Add(new UserWarehouseAccess(tenant.Id, inventoryStaff.Id, main.Id));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo tenant '{Tenant}' with 4 users and 3 warehouses", tenant.Name);

        await SeedModuleDataAsync(db, tenant.Id, logger, ct);
    }

    private static async Task SeedModuleDataAsync(AppDbContext db, Guid tenantId, ILogger logger, CancellationToken ct)
    {
        await DemoCatalog.SeedAsync(db, tenantId, logger, ct);

        var ownerId = await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.RoleId == SystemRoles.OwnerId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        await DemoInventory.SeedAsync(db, tenantId, ownerId, logger, ct);
        await DemoPurchasing.SeedAsync(db, tenantId, ownerId, logger, ct);
        await DemoPurchasing.SyncSequencesAsync(db, tenantId, ct);
    }
}
