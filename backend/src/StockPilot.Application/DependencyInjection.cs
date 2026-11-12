using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace StockPilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped);

        // Use-case handlers are plain classes named *Handler (no mediator library, see assumption A9).
        // Controllers depend on the concrete handler they need, which keeps "go to definition" working.
        foreach (var type in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            services.AddScoped(type);
        }

        services.AddScoped<Common.Security.PermissionChecker>();
        services.AddScoped<Features.Catalog.PermissionCheckerAccessor>();
        services.AddScoped<Features.Warehouses.WarehouseAccessService>();
        services.AddScoped<Features.Inventory.StockOperations>();
        services.AddScoped<Features.Reports.ReportClock>();
        services.AddScoped<Features.Settings.UserAssignment>();
        services.AddScoped<Features.Notifications.NotificationPublisher>();
        services.AddScoped<Features.Notifications.LowStockAlerts>();
        foreach (var job in Features.Platform.PlatformJobs.All)
        {
            services.AddScoped(job.JobType); // resolved by name from a fresh scope per run
        }
        services.AddScoped<Features.Auth.Common.AuthTokenIssuer>();
        services.AddScoped<Features.Auth.Common.AuthEmailService>();

        return services;
    }
}
