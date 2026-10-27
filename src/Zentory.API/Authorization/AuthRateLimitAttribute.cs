using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Authorization;

// Límite de intentos de una acción (PLAN-0018 D-13, RF-29, RF-81 f; SR-0018.29). Es un filtro de acción y no una
// política del rate limiter de ASP.NET: corre después de la autorización (RF-81 e: la policy de rol va primero) y del
// model binding, así que puede usar el email del cuerpo en la clave. Si alguna regla está llena responde 429
// { code: "RATE_LIMITED" } con Retry-After y la acción no se ejecuta.
//
// Una acción puede llevar varias (p. ej. reauth y organization-delete); cada una cuenta el intento por separado.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthRateLimitAttribute(string policy) : Attribute, IAsyncActionFilter
{
    public const string RateLimitedCode = "RATE_LIMITED";

    private const string EmailName      = "Email";
    private const string ActiveOrgClaim = "active_org_id";

    public string Policy { get; } = policy;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var limiter  = context.HttpContext.RequestServices.GetRequiredService<AuthAttemptLimiter>();
        var decision = limiter.TryAcquire(Policy, SubjectOf(context));

        if (!decision.IsAllowed)
        {
            context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(decision.RetryAfter);
            context.Result = new ObjectResult(new
            {
                error   = "rate_limited",
                message = "Demasiados intentos. Intenta de nuevo más tarde.",
                code    = RateLimitedCode,
                details = (object[]?)null,
            })
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };
            return;
        }

        await next();
    }

    // Segundos enteros hacia arriba, nunca 0: un cliente que reintenta a los 0 s volvería a chocar.
    private static string RetryAfterSeconds(TimeSpan retryAfter)
        => Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private static AuthAttemptSubject SubjectOf(ActionExecutingContext context)
        => new(
            UserId: context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Ip:     context.HttpContext.Connection.RemoteIpAddress?.ToString(),
            Email:  EmailOf(context.ActionArguments),
            OrganizationId: context.HttpContext.User.FindFirstValue(ActiveOrgClaim));

    // El email ya enlazado: un argumento string llamado email o la propiedad Email de un DTO del cuerpo. No se lee el
    // cuerpo crudo ni se registra.
    private static string? EmailOf(IDictionary<string, object?> arguments)
    {
        foreach (var (name, value) in arguments)
        {
            if (value is string text && string.Equals(name, EmailName, StringComparison.OrdinalIgnoreCase))
                return text;

            var property = value?.GetType().GetProperty(EmailName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
                return (string?)property.GetValue(value);
        }
        return null;
    }
}
