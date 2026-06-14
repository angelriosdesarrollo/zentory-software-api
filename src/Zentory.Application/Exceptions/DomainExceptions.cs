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

public enum ForbiddenReason { PlanRequired, AccountTypeRequired, TenantMismatch }

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
            ForbiddenReason.AccountTypeRequired => "This feature is not available for your account type.",
            ForbiddenReason.TenantMismatch      => "Access denied.",
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
    public string   FeatureKey  { get; }
    public int      Limit       { get; }
    public int      Used        { get; }
    public DateTime NextResetAt { get; }
    public QuotaExceededException(string featureKey, int limit, int used, DateTime nextResetAt)
        : base($"Quota exceeded for feature '{featureKey}'.")
    {
        FeatureKey  = featureKey;
        Limit       = limit;
        Used        = used;
        NextResetAt = nextResetAt;
    }
}
