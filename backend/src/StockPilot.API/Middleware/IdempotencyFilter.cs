using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Infrastructure.Persistence;

namespace StockPilot.API.Middleware;

/// <summary>
/// Marks a write endpoint as idempotent: clients MUST send an <c>Idempotency-Key</c> header (a UUID per
/// user action). Sending the same request again with the same key returns the original response instead of
/// executing twice, which is what makes a double-clicked "Stock out 10" move stock exactly once.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyFilter));

internal sealed class IdempotencyFilter(
    IdempotencyStore store,
    ICurrentUser currentUser,
    IOptions<JsonOptions> jsonOptions,
    ProblemDetailsFactory problems) : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString().Trim();

        if (key.Length is 0 or > IdempotencyRecord.KeyMaxLength || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            context.Result = Problem(http, StatusCodes.Status400BadRequest, "IDEMPOTENCY_KEY_REQUIRED",
                $"Send a unique {HeaderName} header (letters, digits, '-' or '_', up to {IdempotencyRecord.KeyMaxLength} characters).");
            return;
        }

        if (currentUser.TenantId is not { } tenantId)
        {
            await next();
            return;
        }

        var hash = HashRequest(context);
        var lookup = await store.ClaimAsync(tenantId, currentUser.UserId, key, hash, http.RequestAborted);

        switch (lookup.Claim)
        {
            case IdempotencyClaim.Completed:
                http.Response.Headers["Idempotent-Replayed"] = "true";
                context.Result = new ContentResult
                {
                    StatusCode = lookup.StatusCode,
                    Content = lookup.ResponseBody,
                    ContentType = "application/json; charset=utf-8",
                };
                return;

            case IdempotencyClaim.InProgress:
                context.Result = Problem(http, StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS",
                    "This request is already being processed. Wait a moment, then check the result.");
                return;
        }

        var executed = await next();

        if (executed.Result is ObjectResult { StatusCode: null or (>= 200 and < 300) } ok && executed.Exception is null)
        {
            var body = JsonSerializer.Serialize(ok.Value, jsonOptions.Value.JsonSerializerOptions);
            await store.CompleteAsync(tenantId, key, ok.StatusCode ?? StatusCodes.Status200OK, body, CancellationToken.None);
        }
        else
        {
            // Failed (validation, insufficient stock, server error): forget the key so the client can retry it.
            await store.ReleaseAsync(tenantId, key, CancellationToken.None);
        }
    }

    /// <summary>Hash of the route and the bound body/route/query values, so a reused key with a different request is detected.</summary>
    private string HashRequest(ActionExecutingContext context)
    {
        var parameters = context.ActionDescriptor.Parameters
            .OfType<ControllerParameterDescriptor>()
            .Where(p => p.BindingInfo?.BindingSource is { } source && (source == BindingSource.Body || source == BindingSource.Path || source == BindingSource.Query))
            .ToDictionary(p => p.Name, p => context.ActionArguments.TryGetValue(p.Name, out var value) ? value : null);

        var payload = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}\n{JsonSerializer.Serialize(parameters, jsonOptions.Value.JsonSerializerOptions)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private ObjectResult Problem(HttpContext http, int status, string code, string detail)
    {
        var problem = problems.CreateProblemDetails(http, status, detail: detail);
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
