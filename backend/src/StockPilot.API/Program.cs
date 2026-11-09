using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using StockPilot.API.Extensions;
using StockPilot.Application;
using StockPilot.Infrastructure;
using StockPilot.Infrastructure.Persistence.Seed;

// A plain console logger catches startup failures (bad config, DB unreachable) before the host's logger exists.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "StockPilot.API"));

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration, builder.Environment)
        .AddApi(builder.Configuration)
        .AddApiAuthentication()
        .AddApiRateLimiting(builder.Configuration);

    var app = builder.Build();

    // ---- HTTP pipeline. ORDER MATTERS: each middleware wraps everything registered after it.
    if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    {
        app.UseForwardedHeaders(); // must run first so later middleware sees the real client IP/scheme
    }

    // After UseForwardedHeaders: the authenticated proxy's X-Client-IP wins over the Cloudflare hop.
    app.UseMiddleware<StockPilot.API.Middleware.TrustedProxyClientIpMiddleware>();

    app.UseExceptionHandler();      // turns exceptions from anything below into ProblemDetails
    app.UseStatusCodePages();       // bare 401/403/404s get a ProblemDetails body too
    app.UseSerilogRequestLogging(); // one structured log line per request (method, path, status, ms)

    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
    {
        app.UseHsts();
    }

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json (also the source for frontend TS types)
        app.UseSwaggerUI(o =>      // /swagger
        {
            o.SwaggerEndpoint("/openapi/v1.json", "StockPilot API v1");
            o.DocumentTitle = "StockPilot API";
        });
    }

    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter(); // after authentication so the global limiter can partition by user
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

    await app.Services.InitialiseDatabaseAsync();
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "StockPilot API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so integration tests can boot the app with WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
