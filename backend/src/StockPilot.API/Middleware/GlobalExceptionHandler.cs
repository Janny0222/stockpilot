using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Common;

namespace StockPilot.API.Middleware;

/// <summary>
/// The single place exceptions become HTTP responses (RFC 9457 ProblemDetails). Handlers throw
/// meaningful exceptions; controllers stay free of try/catch. Every body includes a stable "code"
/// for the frontend and a "traceId" that matches the server log line.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, detail) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "Validation failed", "One or more fields are invalid."),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, "Not found", e.Message),
            AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, e.Code, "Unauthorized", e.Message),
            TenantIsolationException e => (StatusCodes.Status403Forbidden, e.Code, "Forbidden", "You do not have access to this resource."),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, "Forbidden", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, "Conflict", e.Message),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, "Business rule violated", e.Message),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "CLIENT_CLOSED_REQUEST", "Request cancelled", "The client closed the request."),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Server error",
                environment.IsDevelopment() ? exception.Message : "An unexpected error occurred."),
        };

        if (status >= 500 || exception is TenantIsolationException)
        {
            logger.LogError(exception, "Unhandled {ExceptionType} → {Status}", exception.GetType().Name, status);
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;

        if (exception is ValidationException validation)
        {
            // { "errors": { "email": ["Email is not valid."] } }. camelCase keys match the JSON/TS field names.
            problem.Extensions["errors"] = validation.Errors
                .GroupBy(e => JsonNamingPolicyCamel(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static string JsonNamingPolicyCamel(string name) =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(name);
}
