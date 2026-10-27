using Microsoft.AspNetCore.Authorization;
using Zentory.Domain.Constants;

namespace Zentory.API.Authorization;

// Rol exigido en la organización activa (PLAN-0018 RF-15; SR-0018.7, SR-0018.8). Lo evalúa OrgRoleRequirementHandler
// contra IMembershipContext, nunca contra el claim active_org_role. RequiresOwnership agrega la comprobación de
// Organization.OwnerId == sub: el rol guardado no basta para lo irreversible.
public sealed class OrgRoleRequirement : IAuthorizationRequirement
{
    private OrgRoleRequirement(IReadOnlyList<string> allowedRoles, bool requiresOwnership)
    {
        AllowedRoles      = allowedRoles;
        RequiresOwnership = requiresOwnership;
    }

    public IReadOnlyList<string> AllowedRoles      { get; }
    public bool                  RequiresOwnership { get; }

    // Lo irreversible: eliminar la organización, transferir la propiedad, plan y suscripción.
    public static OrgRoleRequirement Owner() => new([OrgRole.Owner], requiresOwnership: true);

    // Administración de la organización: owner o admin.
    public static OrgRoleRequirement Admin() => new([OrgRole.Owner, OrgRole.Admin], requiresOwnership: false);

    // Lectura y operación financiera (RF-66, R-E1): owner o admin. Policy aparte para que los grupos B/C/D la
    // endurezcan sin tocar la de administración.
    public static OrgRoleRequirement FinancialAccess() => new([OrgRole.Owner, OrgRole.Admin], requiresOwnership: false);
}
