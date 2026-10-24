using System.IdentityModel.Tokens.Jwt;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Doble de la sesión del portal. FromToken la arma con los claims de un JWT emitido, igual
// que CollaboratorPortalContext los lee del ClaimsPrincipal en la API.
public sealed class FakeCollaboratorPortalContext : ICollaboratorPortalContext
{
    public bool                IsAuthenticated        { get; init; } = true;
    public string              Email                  { get; init; } = string.Empty;
    public IReadOnlyList<Guid> CollaboratorIds        { get; init; } = [];
    public Guid                ActiveCollaboratorId   { get; init; }
    public Guid                ActiveOrganizationId   { get; init; }
    public string              ActiveOrganizationName { get; init; } = string.Empty;
    public string?             AuthMethod             { get; init; }

    public static FakeCollaboratorPortalContext FromToken(JwtSecurityToken token)
    {
        string? Find(string type) => token.Claims.FirstOrDefault(c => c.Type == type)?.Value;

        return new FakeCollaboratorPortalContext
        {
            Email                  = Find(JwtRegisteredClaimNames.Sub) ?? string.Empty,
            CollaboratorIds        = TestCollaboratorJwt.CollaboratorIds(token),
            ActiveCollaboratorId   = Guid.Parse(Find("active_collaborator_id")!),
            ActiveOrganizationId   = Guid.Parse(Find("active_org_id")!),
            ActiveOrganizationName = Find("active_org_name") ?? string.Empty,
            AuthMethod             = Find("auth_method"),
        };
    }
}
