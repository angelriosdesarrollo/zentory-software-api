using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Zentory.Application.Common.Interfaces;

namespace Zentory.API.Authorization;

// Evalúa OrgRoleRequirement con la membresía de la petición en curso (PLAN-0018 RF-15; SR-0018.7, SR-0018.8,
// SR-0018.40). Se registra scoped: recibe el IMembershipContext de la petición por constructor y nunca lo comparte
// entre peticiones (AuthorizationHandlerScopePolicyTests lo vigila).
//
// Falla cerrado: sin membresía resuelta (acción con [SkipMembershipValidation], sesión de colaborador, fuera de una
// petición) no autoriza, aunque el claim active_org_role diga owner. No hay respaldo al claim.
public sealed class OrgRoleRequirementHandler(IMembershipContext membership) : AuthorizationHandler<OrgRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrgRoleRequirement requirement)
    {
        if (!membership.IsResolved)
            return Task.CompletedTask;

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var sub)
            || sub != membership.UserId)
            return Task.CompletedTask;

        if (!requirement.AllowedRoles.Contains(membership.Role, StringComparer.Ordinal))
            return Task.CompletedTask;

        if (requirement.RequiresOwnership && membership.OwnerUserId != sub)
            return Task.CompletedTask;

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
