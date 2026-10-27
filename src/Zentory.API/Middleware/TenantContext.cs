using System.Security.Claims;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;

namespace Zentory.API.Middleware;

/// <summary>
/// Per-request tenant context populated from JWT claims.
/// </summary>
// El principal se lee de HttpContext en cada acceso, no en el constructor (PLAN-0018 RF-11, ADR-0008 §2): la
// autorización del esquema de colaborador reemplaza HttpContext.User después de que algo (un middleware, el
// AuditInterceptor del DbContext) haya podido resolver este servicio, y el filtro de tenant debe ver el principal final.
// Con un principal de colaborador, IsAuthenticated y OrganizationId valen para el filtro; UserId, Plan, LegalType y
// ActiveOrgRole no tienen sentido y el portal no los usa.
//
// ActiveOrgRole e IsOwner salen de IMembershipContext, la membresía leída de la base en esta petición (PLAN-0018 RF-14,
// SR-0018.7), sin respaldo al claim active_org_role: sin membresía resuelta (ruta excluida, colaborador, sin petición)
// ActiveOrgRole lanza e IsOwner es false.
public sealed class TenantContext : ITenantContext
{
    private const string ActiveOrgClaim = "active_org_id";

    private readonly IHttpContextAccessor _accessor;
    private readonly IMembershipContext   _membership;

    public TenantContext(IHttpContextAccessor accessor, IMembershipContext membership)
    {
        _accessor   = accessor;
        _membership = membership;
    }

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool HasActiveOrganization
        => IsAuthenticated
        && Guid.TryParse(User?.FindFirstValue(ActiveOrgClaim), out var organizationId)
        && organizationId != Guid.Empty;

    public Guid OrganizationId
    {
        get
        {
            var value = User?.FindFirstValue(ActiveOrgClaim)
                ?? throw new InvalidOperationException("Missing active_org_id claim — endpoint requires authentication.");
            return Guid.Parse(value);
        }
    }

    public Guid UserId
    {
        get
        {
            var value = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Missing sub claim — endpoint requires authentication.");
            return Guid.Parse(value);
        }
    }

    public Guid SessionId
        => SessionClaims.TryRead(User, out var sessionId)
            ? sessionId
            : throw new InvalidOperationException("Missing sid claim — endpoint requires a user session.");

    public string UserInitials  => User?.FindFirstValue("initials")        ?? "??";
    // Plan efectivo del owner (RF-40, RF-73; D-26), nunca el claim plan: sin membresía resuelta (ruta excluida,
    // colaborador, sin petición) es free, el mínimo.
    public string Plan          => _membership.IsResolved ? _membership.EffectivePlan : Zentory.Domain.Constants.Plan.Free;
    public string LegalType     => User?.FindFirstValue("legal_type")      ?? Zentory.Domain.Constants.LegalType.Freelance;
    public string ActiveOrgRole => _membership.Role;

    // Owner por rol y por Organization.OwnerId (SR-0018.8): un rol owner sin ser el OwnerId de la organización no basta.
    public bool IsOwner
        => _membership.IsResolved
        && _membership.Role == OrgRole.Owner
        && _membership.OwnerUserId == _membership.UserId;
}
