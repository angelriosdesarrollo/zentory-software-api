namespace Zentory.Application.Exceptions;

public class NotFoundException : Exception
{
    public string ResourceType { get; }
    public NotFoundException(string resourceType, Guid id)
        : base($"{resourceType} with id '{id}' was not found.")
        => ResourceType = resourceType;
    public NotFoundException(string resourceType, string id)
        : base($"{resourceType} with id '{id}' was not found.")
        => ResourceType = resourceType;
}

public enum ForbiddenReason { PlanRequired, LegalTypeRequired, TenantMismatch, EmailNotVerified }

public class ForbiddenException : Exception
{
    public ForbiddenReason Reason { get; }
    public string? RequiredPlan   { get; }
    public ForbiddenException(ForbiddenReason reason, string? requiredPlan = null)
        : base(BuildMessage(reason, requiredPlan))
    {
        Reason       = reason;
        RequiredPlan = requiredPlan;
    }
    private static string BuildMessage(ForbiddenReason reason, string? requiredPlan) =>
        reason switch
        {
            ForbiddenReason.PlanRequired       => $"This feature requires plan '{requiredPlan}'.",
            ForbiddenReason.LegalTypeRequired => "This feature is not available for your account type.",
            ForbiddenReason.TenantMismatch      => "Access denied.",
            ForbiddenReason.EmailNotVerified    => "Verifica tu correo electrónico para continuar.",
            _                                   => "Forbidden."
        };
}

public class ConflictException : Exception
{
    public string Code { get; }
    public ConflictException(string code, string message) : base(message) => Code = code;
}

public record ValidationError(string Field, string Message);

public class ValidationException : Exception
{
    public IEnumerable<ValidationError> Errors { get; }
    public ValidationException(IEnumerable<ValidationError> errors)
        : base("One or more validation errors occurred.")
        => Errors = errors;
}

public class ServiceUnavailableException : Exception
{
    public string Service          { get; }
    public int    RetryAfterSeconds { get; }
    public ServiceUnavailableException(string service, int retryAfterSeconds)
        : base($"Service '{service}' is temporarily unavailable.")
    {
        Service             = service;
        RetryAfterSeconds   = retryAfterSeconds;
    }
}

public class QuotaExceededException : Exception
{
    public string    FeatureKey  { get; }
    public int       Limit       { get; }
    public int       Used        { get; }
    public DateTime? NextResetAt { get; }

    public QuotaExceededException(string featureKey, int limit, int used, DateTime? nextResetAt = null)
        : base($"Quota exceeded for feature '{featureKey}'.")
    {
        FeatureKey  = featureKey;
        Limit       = limit;
        Used        = used;
        NextResetAt = nextResetAt;
    }
}

// 401 con código (PLAN-0018 RF-21, D-9): { error: "unauthorized", code }. Para fallos de autenticación que el cliente
// debe tratar como sesión inválida (p. ej. un refresh token reutilizado), no para credenciales incorrectas (RF-61: 422).
public class UnauthorizedException : Exception
{
    public string Code { get; }
    public UnauthorizedException(string code, string message = "No autorizado.") : base(message) => Code = code;
}

// Sesión reemplazada (PLAN-0018 RF-90 d, e; RNF-10; D-47): el refresh token o el access token son de una sesión que ya no
// es la vigente del usuario porque inició otra. 401 con el cuerpo exacto de SessionReplacedResponse, sin message: el
// mensaje de la excepción es interno y no viaja en la respuesta.
public sealed class SessionReplacedException : Exception
{
    public SessionReplacedException() : base("The session was replaced by a newer one.") { }
}

// Access token de usuario sin sid legible (PLAN-0018 RF-90 d, D-47): token anterior a M2c. 401 { error: "unauthorized" },
// sin code, el mismo cuerpo que escribe el middleware de membresía, para que el cliente siga el camino de RF-18. Lo lanza
// una acción [AllowAnonymous] que compara la sesión en su comando (POST /auth/invitations/accept con JWT, C-16).
public sealed class MissingSessionException : Exception
{
    public MissingSessionException() : base("The access token has no readable session id.") { }
}

public static class SessionCodes
{
    public const string SessionReplaced = "SESSION_REPLACED";
}

// Token de un solo uso inexistente, ya usado o expirado (RF-21, D-9): 422 TOKEN_INVALID con el mismo cuerpo en los
// tres casos, para no revelar cuál fue.
public class InvalidTokenException : Exception
{
    public const string Code = "TOKEN_INVALID";
    public InvalidTokenException() : base("El enlace no es válido o ya expiró.") { }
}

// 429 RATE_LIMITED con Retry-After (RF-21, D-9), cuando el límite lo decide un comando y no el filtro [AuthRateLimit].
public class TooManyRequestsException : Exception
{
    public const string Code = "RATE_LIMITED";
    public int RetryAfterSeconds { get; }
    public TooManyRequestsException(int retryAfterSeconds)
        : base("Demasiados intentos. Intenta de nuevo más tarde.")
        => RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
}
