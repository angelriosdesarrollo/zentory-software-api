using System.Security.Claims;

namespace Zentory.Application.Common.Security;

// Único lector del claim sid del access token de usuario (PLAN-0018 RF-90, D-47). JwtService lo escribe como
// JwtRegisteredClaimNames.Sid ("sid"); con el mapeo de claims de entrada por defecto llega como ClaimTypes.Sid, así que
// se aceptan los dos nombres. Lo usan TenantContext.SessionId y el middleware de membresía; ningún otro código lee el
// claim.
public static class SessionClaims
{
    public const string Sid = "sid";

    // Un sid que no es un Guid en formato D o que es Guid.Empty no es legible: el llamador lo trata como token sin sid.
    public static bool TryRead(ClaimsPrincipal? principal, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        if (principal is null)
            return false;

        var value = principal.FindFirst(Sid)?.Value ?? principal.FindFirst(ClaimTypes.Sid)?.Value;
        return Guid.TryParseExact(value, "D", out sessionId) && sessionId != Guid.Empty;
    }
}
