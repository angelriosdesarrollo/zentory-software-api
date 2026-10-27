using System.Globalization;
using System.Net;
using System.Text.Json;
using Zentory.API.Http;
using Zentory.Application.Common.Security;
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
        // Sesión reemplazada (PLAN-0018 RF-90 e, RNF-10): no es un error del servidor. El log nombra solo la ruta; nunca el
        // sid de la otra sesión ni su IP. El cuerpo exacto lo escribe SessionReplacedResponse.
        if (exception is SessionReplacedException)
        {
            _logger.LogInformation("Sesión reemplazada. Path: {Path}", context.Request.Path);
            await SessionReplacedResponse.WriteAsync(context.Response);
            return;
        }

        // Token sin sid en una acción que compara la sesión en su comando (RF-90 d, C-16): el mismo 401 sin code que
        // escribe el middleware de membresía.
        if (exception is MissingSessionException)
        {
            context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(MembershipValidationMiddleware.MissingSessionBody);
            return;
        }

        _logger.LogError(exception, "Unhandled exception. Path: {Path}", context.Request.Path);

        var (statusCode, body) = exception switch
        {
            NotFoundException e => (
                HttpStatusCode.NotFound,
                Error("not_found", "El recurso solicitado no existe.", null, null)),

            // Mismo cuerpo para todo fallo de reautenticación (PLAN-0018 RF-81 d): 403 y no 401, porque el cliente trata
            // todo 401 como sesión caducada. El mensaje es fijo y no dice qué falló.
            ReauthenticationFailedException e => (
                HttpStatusCode.Forbidden,
                Error("forbidden", e.Message, ReauthenticationFailedException.Code, null)),

            ForbiddenException e => (
                HttpStatusCode.Forbidden,
                (object)new
                {
                    error        = "forbidden",
                    message      = e.Message,
                    code         = ForbiddenCode(e.Reason),
                    requiredPlan = e.RequiredPlan,
                    details      = (object[]?)null
                }),

            // PLAN-0018 D-9 (RF-21): 401 con código, token de un solo uso inválido y límite de intentos.
            UnauthorizedException e => (
                HttpStatusCode.Unauthorized,
                Error("unauthorized", e.Message, e.Code, null)),

            InvalidTokenException e => (
                HttpStatusCode.UnprocessableEntity,
                Error("token_invalid", e.Message, InvalidTokenException.Code, null)),

            TooManyRequestsException e => (
                HttpStatusCode.TooManyRequests,
                Error("too_many_requests", e.Message, TooManyRequestsException.Code, null)),

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
        if (exception is TooManyRequestsException limited)
            context.Response.Headers.RetryAfter = limited.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }

    private static object Error(string error, string message, string? code, object[]? details) =>
        new { error, message, code, details };

    private static string ForbiddenCode(ForbiddenReason reason) => reason switch
    {
        ForbiddenReason.PlanRequired     => "PLAN_REQUIRED",
        ForbiddenReason.EmailNotVerified => "EMAIL_NOT_VERIFIED",
        _                                => "ACCESS_DENIED",
    };
}
