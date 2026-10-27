namespace Zentory.Application.Common.Interfaces;

public interface ITenantContext
{
    bool   IsAuthenticated        { get; }
    // Hay un active_org_id que es un Guid válido (PLAN-0018 RF-11). Los filtros de tenant lo consultan antes de leer
    // OrganizationId, que lanza sin él; un principal autenticado sin organización activa no ve filas (SR-0018.6).
    bool   HasActiveOrganization  { get; }
    Guid   OrganizationId         { get; }  // active org (reads active_org_id claim)
    Guid   UserId                 { get; }
    // Sesión del token que llama (PLAN-0018 RF-90 c, D-47): su sid, leído con SessionClaims. Lanza si no lo trae. Con él
    // se emite el access token de switch, nunca con User.CurrentSessionId.
    Guid   SessionId              { get; }
    string UserInitials           { get; }
    // Plan efectivo de la organización activa: el del owner, IMembershipContext.EffectivePlan (PLAN-0018 RF-40, RF-73;
    // D-26). Nunca el claim plan. Free sin membresía resuelta.
    string Plan                   { get; }
    string LegalType              { get; }
    string ActiveOrgRole          { get; }  // Un valor de OrgRole (Zentory.Domain.Constants)
    bool   IsOwner                { get; }
}
