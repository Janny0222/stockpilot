using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Options;
using StockPilot.Infrastructure.Email;
using StockPilot.Infrastructure.Identity;
using StockPilot.Infrastructure.Persistence;
using StockPilot.Infrastructure.Persistence.Interceptors;

namespace StockPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        // ---- Options (validated at startup: a misconfigured app fails fast instead of at first request)
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateOnStart();
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName)).ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => environment.IsDevelopment() || environment.IsEnvironment("Testing")
                     || !o.SigningKey.Contains(JwtOptions.DevelopmentKeyMarker, StringComparison.Ordinal),
                "The development JWT signing key must not be used outside Development. Set Jwt__SigningKey.")
            .ValidateOnStart();

        // ---- Persistence
        services.AddScoped<TenantIsolationInterceptor>();
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    // Neon suspends idle compute; the first connection after wake-up can fail transiently.
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    sp.GetRequiredService<TenantIsolationInterceptor>(),
                    sp.GetRequiredService<AuditingInterceptor>());

            if (environment.IsDevelopment())
            {
                // Deliberately NOT EnableSensitiveDataLogging(): it writes parameter values (password
                // hashes, tokens) into logs, and logs get shared in bug reports.
                options.EnableDetailedErrors();
            }
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // ---- Identity
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ISecureTokenFactory, SecureTokenFactory>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ISessionValidator, SessionValidator>();

        // ---- Inventory
        services.AddScoped<IStockLocker, Inventory.StockLocker>();
        services.AddScoped<IDocumentNumberGenerator, Inventory.DocumentNumberGenerator>();
        services.AddScoped<Persistence.IdempotencyStore>();

        // ---- Files & images
        services.AddScoped<IFileStorage, Storage.DatabaseFileStorage>();
        services.AddSingleton<IImageProcessor, Storage.SkiaImageProcessor>();

        // ---- Email
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<LoggingEmailSender>();
        services.AddSingleton<IEmailSender>(sp =>
            sp.GetRequiredService<IOptions<EmailOptions>>().Value.Provider.Equals("Log", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<LoggingEmailSender>()
                : sp.GetRequiredService<SmtpEmailSender>());

        return services;
    }
}
