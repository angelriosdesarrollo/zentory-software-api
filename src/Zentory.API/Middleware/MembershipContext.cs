using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;

namespace Zentory.API.Middleware;

// Implementación scoped de IMembershipContext (PLAN-0018 RF-14; D-26). Solo MembershipValidationMiddleware la puebla, una
// vez por petición y después de comprobar la membresía y resolver el plan del owner en la base.
public sealed class MembershipContext : IMembershipContext
{
    private Resolved? _resolved;

    public bool IsResolved => _resolved is not null;

    public Guid    OrganizationId   => Current.OrganizationId;
    public Guid    UserId           => Current.UserId;
    public string  Role             => Current.Role;
    public Guid?   OwnerUserId      => Current.OwnerUserId;
    public string  EffectivePlan    => Current.EffectivePlan;
    public string? OwnerDisplayName => Current.OwnerDisplayName;

    // Sin plan explícito la membresía queda en free: un llamador que no lo resolvió nunca concede más de lo mínimo.
    public void Resolve(
        Guid    organizationId,
        Guid    userId,
        string  role,
        Guid?   ownerUserId,
        string? effectivePlan    = null,
        string? ownerDisplayName = null)
    {
        if (_resolved is not null)
            throw new InvalidOperationException("La membresía de la petición ya se resolvió.");
        _resolved = new Resolved(organizationId, userId, role, ownerUserId, effectivePlan ?? Plan.Free, ownerDisplayName);
    }

    private Resolved Current => _resolved
        ?? throw new InvalidOperationException(
            "Membership not resolved — excluded endpoint, collaborator session or no request in progress.");

    private sealed record Resolved(
        Guid OrganizationId, Guid UserId, string Role, Guid? OwnerUserId, string EffectivePlan, string? OwnerDisplayName);
}
