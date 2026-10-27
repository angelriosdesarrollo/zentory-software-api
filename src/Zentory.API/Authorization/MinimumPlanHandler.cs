using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

namespace Zentory.API.Authorization;

// Evalúa MinimumPlanRequirement con el plan efectivo de la organización activa (PLAN-0018 RF-40, RF-73, RF-75 a; D-26):
// IMembershipContext.EffectivePlan, el plan del owner leído de la base en esta petición. Nunca lee el claim plan del token,
// que es informativo (PlanClaimPolicyTests). Se registra scoped, como OrgRoleRequirementHandler, porque recibe la
// membresía de la petición por constructor (SR-0018.40; AuthorizationHandlerScopePolicyTests).
//
// Falla cerrado: sin membresía resuelta (acción con [SkipMembershipValidation], sesión de colaborador, fuera de una
// petición), o con un principal que no es el de la membresía, no autoriza. Un plan desconocido tampoco.
public sealed class MinimumPlanHandler(IMembershipContext membership) : AuthorizationHandler<MinimumPlanRequirement>
{
    private static readonly string[] PlanOrder = [Plan.Free, Plan.Pro, Plan.Studio];

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumPlanRequirement      requirement)
    {
        if (!membership.IsResolved)
            return Task.CompletedTask;

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var sub)
            || sub != membership.UserId)
            return Task.CompletedTask;

        if (Meets(membership.EffectivePlan, requirement.RequiredPlan))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    // Orden free < pro < studio. Un plan fuera del catálogo no alcanza ningún requisito.
    public static bool Meets(string effectivePlan, string requiredPlan)
    {
        var effective = Array.IndexOf(PlanOrder, effectivePlan);
        var required  = Array.IndexOf(PlanOrder, requiredPlan);
        return effective >= 0 && required >= 0 && effective >= required;
    }
}
