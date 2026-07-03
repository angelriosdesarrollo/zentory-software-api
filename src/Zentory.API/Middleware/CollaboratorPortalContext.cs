using System.Security.Claims;
using Zentory.Application.Common.Interfaces;

namespace Zentory.API.Middleware;

/// <summary>
/// Contexto de sesión del portal de autoservicio de colaboradores, poblado desde las
/// claims del JWT del esquema "CollaboratorScheme" — separado de TenantContext, que
/// asume claims de empresa (active_org_role, plan, legal_type) sin sentido acá.
/// </summary>
public sealed class CollaboratorPortalContext : ICollaboratorPortalContext
{
    private readonly ClaimsPrincipal? _user;

    public CollaboratorPortalContext(IHttpContextAccessor accessor)
    {
        _user = accessor.HttpContext?.User;
    }

    public bool IsAuthenticated => _user?.Identity?.IsAuthenticated ?? false;

    // Sub (email) se remapea a ClaimTypes.NameIdentifier por el manejo por defecto de
    // JwtSecurityTokenHandler — mismo comportamiento que ya asume TenantContext.UserId.
    public string Email
        => _user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Missing sub claim — endpoint requires collaborator authentication.");

    public IReadOnlyList<Guid> CollaboratorIds
    {
        get
        {
            var raw = _user?.FindFirstValue("collaborator_ids") ?? string.Empty;
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Guid.Parse)
                .ToList();
        }
    }

    public Guid ActiveCollaboratorId
    {
        get
        {
            var value = _user?.FindFirstValue("active_collaborator_id")
                ?? throw new InvalidOperationException("Missing active_collaborator_id claim.");
            return Guid.Parse(value);
        }
    }

    public Guid ActiveOrganizationId
    {
        get
        {
            var value = _user?.FindFirstValue("active_org_id")
                ?? throw new InvalidOperationException("Missing active_org_id claim.");
            return Guid.Parse(value);
        }
    }

    public string ActiveOrganizationName => _user?.FindFirstValue("active_org_name") ?? string.Empty;
}
