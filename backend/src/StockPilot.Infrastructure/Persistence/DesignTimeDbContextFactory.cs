using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using StockPilot.Application.Common.Abstractions;

namespace StockPilot.Infrastructure.Persistence;

/// <summary>
/// Lets `dotnet ef` (migrations add / database update) build the model without starting the web app.
///
/// Connection string, first match wins:
///   1. STOCKPILOT_DB environment variable (CI, one-off commands)
///   2. the API project's User Secrets → ConnectionStrings:Database (e.g. your Neon dev branch)
///   3. local PostgreSQL on port 5433 (docker compose / portable install)
/// `migrations add` never connects; only `database update` / `migrations list` do.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // Must match <UserSecretsId> in StockPilot.API.csproj.
    private const string ApiUserSecretsId = "stockpilot-api-7c1e2b9a";

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(ApiUserSecretsId)
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("STOCKPILOT_DB")
            ?? configuration.GetConnectionString("Database")
            ?? "Host=localhost;Port=5433;Database=stockpilot;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, new DesignTimeUser());
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? TenantId => null;
        public Guid? RoleId => null;
        public bool IsAuthenticated => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
        public string? CorrelationId => null;
    }
}
