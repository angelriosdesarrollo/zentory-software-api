using Zentory.Application.Auth.DTOs;

namespace Zentory.Application.Auth.Services;

// Única lectura de las membresías de un usuario en todas sus organizaciones (PLAN-0018 RF-12, ADR-0008 §3). El filtro
// de tenant de OrganizationMember solo deja ver la organización activa del token; las consultas que por diseño cruzan
// organizaciones del mismo usuario (listado de organizaciones, cambio de organización y, desde T8, la emisión de
// sesión y el middleware de membresía) pasan por aquí, que es el único sitio de Auth con IgnoreQueryFilters.
public interface IUserMembershipReader
{
    // Membresías vigentes (sin borrado lógico) del usuario, con su organización, el plan efectivo y el nombre de su owner.
    Task<IReadOnlyList<UserMembership>> ListActiveAsync(Guid userId, CancellationToken ct = default);

    // Membresía vigente (organizationId, userId), o null si no existe o está dada de baja. Trae en la misma consulta la
    // sesión vigente del usuario (CurrentSessionId), para que el middleware compare el sid sin otra lectura (RF-90 d,
    // RNF-1; D-47).
    Task<UserMembership?> FindActiveAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
}

public sealed record UserMembership(
    Guid     OrganizationId,
    string   OrganizationName,
    string   LegalType,
    Guid?    OwnerId,
    string   Plan,
    string   Role,
    DateTime JoinedAt,
    string?  OwnerName = null,
    Guid?    CurrentSessionId = null)
{
    public OrgMembershipDto ToDto()
        => new(OrganizationId.ToString(), OrganizationName, LegalType, Plan, Role, JoinedAt.ToString("O"), OwnerName);
}
