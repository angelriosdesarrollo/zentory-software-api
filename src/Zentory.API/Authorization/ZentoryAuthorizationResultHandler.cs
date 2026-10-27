using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Zentory.Domain.Constants;

namespace Zentory.API.Authorization;

// 403 de las policies con cuerpo (PLAN-0018 RF-58; D-8; DEC-0010), con el formato de GlobalExceptionHandlingMiddleware:
// { error: "forbidden", message, code, requiredPlan, details }. El código sale de AuthorizationFailure.FailedRequirements
// con precedencia tipo de cuenta → plan → rol:
//   - requisito del claim legal_type        → ACCESS_DENIED;
//   - MinimumPlanRequirement                → PLAN_REQUIRED con requiredPlan (el más alto de los que fallaron);
//   - OrgRoleRequirement u otro requisito   → ACCESS_DENIED.
// El challenge (401) y el éxito siguen en el manejador por defecto: los 401 no cambian.
public sealed class ZentoryAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string AccessDeniedCode = "ACCESS_DENIED";
    public const string PlanRequiredCode = "PLAN_REQUIRED";

    public const string LegalTypeClaim = "legal_type";

    private static readonly string[] PlanOrder = [Plan.Free, Plan.Pro, Plan.Studio];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate           next,
        HttpContext               context,
        AuthorizationPolicy       policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var body = ForbiddenBodyFor(authorizeResult.AuthorizationFailure);

        context.Response.StatusCode  = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }

    // Público para que los tests verifiquen la precedencia sin pasar por HTTP.
    public static ForbiddenBody ForbiddenBodyFor(AuthorizationFailure? failure)
    {
        var failed = failure?.FailedRequirements.ToList() ?? [];

        if (failed.Any(IsLegalTypeRequirement))
            return ForbiddenBody.AccessDenied("This feature is not available for your account type.");

        var requiredPlan = failed.OfType<MinimumPlanRequirement>()
            .Select(r => r.RequiredPlan)
            .OrderByDescending(plan => Array.IndexOf(PlanOrder, plan))
            .FirstOrDefault();
        if (requiredPlan is not null)
            return ForbiddenBody.PlanRequired(requiredPlan);

        return ForbiddenBody.AccessDenied("Access denied.");
    }

    private static bool IsLegalTypeRequirement(IAuthorizationRequirement requirement)
        => requirement is ClaimsAuthorizationRequirement { ClaimType: LegalTypeClaim };
}

public sealed record ForbiddenBody(string Error, string Message, string Code, string? RequiredPlan, object[]? Details)
{
    public static ForbiddenBody AccessDenied(string message)
        => new("forbidden", message, ZentoryAuthorizationResultHandler.AccessDeniedCode, null, null);

    public static ForbiddenBody PlanRequired(string requiredPlan)
        => new("forbidden", $"This feature requires plan '{requiredPlan}'.",
            ZentoryAuthorizationResultHandler.PlanRequiredCode, requiredPlan, null);
}
