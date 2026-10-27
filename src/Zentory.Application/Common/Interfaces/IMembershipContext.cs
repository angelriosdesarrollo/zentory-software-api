namespace Zentory.Application.Common.Interfaces;

// Membresía vigente de la petición en curso, leída de la base por MembershipValidationMiddleware (PLAN-0018 RF-14,
// ADR-0006 §1-2 anotado). Scoped: cada petición tiene la suya y nunca se cachea entre peticiones. Es la única fuente del
// rol en la organización activa; el claim active_org_role no autoriza nada (SR-0018.7).
//
// Plan efectivo (RF-40, RF-73; ADR-0006 parte 3, anotado C2-f; D-26): el plan de la organización activa es el del owner,
// resuelto en la misma pasada que la membresía con su suscripción active/trialing, o free sin ella. Es la única fuente del
// plan para autorizar: el claim plan del token es informativo y nada lo lee para decidir (PlanClaimPolicyTests). Un
// cambio de plan rige en la petición siguiente, sin re-login ni refresh.
//
// IsResolved es false fuera de una petición, en las acciones con [SkipMembershipValidation] y en la sesión de
// colaborador: ahí Role, OrganizationId, UserId, EffectivePlan y OwnerDisplayName lanzan, para que nada autorice por rol
// ni por plan sin haber validado la membresía.
public interface IMembershipContext
{
    bool    IsResolved       { get; }
    Guid    OrganizationId   { get; }
    Guid    UserId           { get; }
    string  Role             { get; }  // OrgRole.Owner | OrgRole.Admin | OrgRole.Member, de OrganizationMember.Role
    Guid?   OwnerUserId      { get; }  // Organization.OwnerId de la organización activa (SR-0018.8)
    string  EffectivePlan    { get; }  // Plan.Free | Plan.Pro | Plan.Studio, el del owner (D-26)
    string? OwnerDisplayName { get; }  // nombre del owner para "Plan del propietario" (RF-76 d); null sin owner
}
