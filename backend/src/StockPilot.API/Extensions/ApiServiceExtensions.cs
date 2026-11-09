using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StockPilot.API.Authorization;
using StockPilot.API.Common;
using StockPilot.API.Middleware;
using StockPilot.API.OpenApi;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Infrastructure.Identity;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.API.Extensions;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
}

internal static class ApiServiceExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // Strict numbers ("5" is not accepted as 5) and enums as strings ("Active", not 1).
        // Set for both MVC (controllers) and the minimal-API options the OpenAPI generator reads,
        // so the generated TypeScript types match what the API actually sends.
        services.AddControllers().AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Instance = ctx.HttpContext.Request.Path;
            ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Model-binding failures (e.g. malformed JSON) use the same ProblemDetails shape + code.
        services.Configure<ApiBehaviorOptions>(o =>
        {
            var defaultFactory = o.InvalidModelStateResponseFactory;
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var result = (ObjectResult)defaultFactory(ctx);
                if (result.Value is ProblemDetails pd)
                {
                    pd.Extensions["code"] = "VALIDATION_FAILED";
                }

                return result;
            };
        });

        services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        services.AddCors(o => o.AddDefaultPolicy(policy => policy
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Requested-With")
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(1))));

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // Render/Cloudflare terminate TLS and forward the client IP. Their proxy IPs aren't fixed,
            // so the known-proxy list is cleared. Only enable this behind a trusted proxy (config flag).
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        return services;
    }

    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured via options so it reads the *validated* JwtOptions (and tests can override them).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false; // keep "sub"/"tid" as-is instead of long WS-* URIs
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = StockPilotClaims.UserId,
                };
                options.Events = new JwtBearerEvents
                {
                    // A signature-valid token is not enough: the user may have been deactivated, changed
                    // their password, or their company suspended since the token was issued.
                    OnTokenValidated = async ctx =>
                    {
                        var principal = ctx.Principal!;
                        if (!Guid.TryParse(principal.FindFirst(StockPilotClaims.UserId)?.Value, out var userId) ||
                            principal.FindFirst(StockPilotClaims.SecurityStamp)?.Value is not { } stamp)
                        {
                            ctx.Fail("Malformed token.");
                            return;
                        }

                        var validator = ctx.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
                        if (!await validator.IsValidAsync(userId, stamp, ctx.HttpContext.RequestAborted))
                        {
                            ctx.Fail("Session is no longer valid.");
                        }
                    },
                };
            });

        services.AddAuthorizationBuilder()
            // Secure by default: every endpoint requires an authenticated user unless marked [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var authLimit = configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
        var globalLimit = configuration.GetValue("RateLimiting:GlobalPermitLimit", 300);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Login/register/forgot-password: tight, per client IP. Per-account brute force is
            // additionally stopped by account lockout.
            o.AddPolicy(RateLimitPolicies.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));

            // Everything else: generous, per user when signed in, otherwise per IP.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.FindFirst(StockPilotClaims.UserId)?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = globalLimit, Window = TimeSpan.FromMinutes(1) }));

            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "You're doing that too often. Please wait a moment and try again.",
                };
                problem.Extensions["code"] = "RATE_LIMITED";
                await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = ctx.HttpContext, ProblemDetails = problem });
            };
        });

        return services;
    }
}
