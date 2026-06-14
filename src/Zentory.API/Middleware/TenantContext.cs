using System.Security.Claims;
using Zentory.Application.Common.Interfaces;

namespace Zentory.API.Middleware;

/// <summary>
/// Per-request tenant context populated from JWT claims.
/// Properties are evaluated lazily so construction is safe for anonymous endpoints.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private readonly ClaimsPrincipal? _user;

    public TenantContext(IHttpContextAccessor accessor)
    {
        _user = accessor.HttpContext?.User;
    }

    public bool IsAuthenticated => _user?.Identity?.IsAuthenticated ?? false;

    public Guid OrganizationId
    {
        get
        {
            var value = _user?.FindFirstValue("organization_id")
                ?? throw new InvalidOperationException("Missing organization_id claim — endpoint requires authentication.");
            return Guid.Parse(value);
        }
    }

    public Guid UserId
    {
        get
        {
            var value = _user?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("Missing sub claim — endpoint requires authentication.");
            return Guid.Parse(value);
        }
    }

    public string UserInitials => _user?.FindFirstValue("initials")     ?? "??";
    public string Plan        => _user?.FindFirstValue("plan")         ?? "free";
    public string AccountType => _user?.FindFirstValue("account_type") ?? "freelance";
}
