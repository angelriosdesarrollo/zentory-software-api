using System.Net;
using System.Text.Json;
using Zentory.Application.Exceptions;

namespace Zentory.API.Middleware;

public sealed class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next   = next;
        _logger = logger;
        _env    = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception. Path: {Path}", context.Request.Path);

        var (statusCode, body) = exception switch
        {
            NotFoundException e => (
                HttpStatusCode.NotFound,
                Error("not_found", "El recurso solicitado no existe.", null, null)),

            ForbiddenException e => (
                HttpStatusCode.Forbidden,
                (object)new
                {
                    error        = "forbidden",
                    message      = e.Message,
                    code         = e.Reason == ForbiddenReason.PlanRequired ? "PLAN_REQUIRED" : "ACCESS_DENIED",
                    requiredPlan = e.RequiredPlan,
                    details      = (object[]?)null
                }),

            ValidationException e => (
                HttpStatusCode.UnprocessableEntity,
                Error("validation_error", "Los datos enviados no son válidos.", null,
                    e.Errors.Select(v => new { field = v.Field, message = v.Message }).ToArray<object>())),

            ConflictException e => (
                HttpStatusCode.Conflict,
                Error("conflict", e.Message, e.Code, null)),

            ServiceUnavailableException e => (
                HttpStatusCode.ServiceUnavailable,
                Error("service_unavailable", e.Message, "SERVICE_UNAVAILABLE", null)),

            QuotaExceededException e => (
                HttpStatusCode.UnprocessableEntity,
                (object)new
                {
                    error      = "quota_exceeded",
                    message    = e.Message,
                    code       = "QUOTA_EXCEEDED",
                    featureKey = e.FeatureKey,
                    details    = new object[] { new { limit = e.Limit, used = e.Used, resetAt = e.NextResetAt } }
                }),

            _ => (
                HttpStatusCode.InternalServerError,
                Error("internal_error",
                    _env.IsProduction() ? "Ha ocurrido un error interno." : exception.Message,
                    null, null))
        };

        context.Response.StatusCode  = (int)statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }

    private static object Error(string error, string message, string? code, object[]? details) =>
        new { error, message, code, details };
}
