using Zentory.Application.Auth.DTOs;
using Zentory.Domain.Entities;

namespace Zentory.Application.Auth.Services;

// Emisión única de sesión (PLAN-0018 D-16, A-5; RF-12, RF-16, RF-70): elige la organización activa, arma las membresías
// del usuario en todas sus organizaciones con IUserMembershipReader (nunca a través del filtro de tenant, que con un
// Bearer válido en los endpoints [AllowAnonymous] las truncaría a la organización del token) y emite el par de tokens.
//
// Organización activa: preferredOrgId si el usuario tiene ahí una membresía vigente; si no, la primera propia; si no, la
// más antigua. Login y Google pasan User.LastActiveOrganizationId; registro, la organización recién creada; refresh, la
// que pide el cliente (RF-16). Sin membresías vigentes (RF-85, RF-61; D-44): si el usuario es owner de organizaciones
// eliminadas aún restaurables, emite la sesión sin organización activa (token sin active_org_id, user.activeOrgId null,
// sin membresías y con refresh token normal); si no, lanza NoActiveMembershipException, que cada comando traduce a su
// 4xx genérico, antes de escribir nada.
//
// Sesión única (RF-90, D-47): iniciar una sesión (login, Google, registro) la deja como la vigente del usuario y revoca
// todos sus refresh anteriores; continuarla (refresh) conserva su sid y no toca nada más.
public interface IAuthSessionIssuer
{
    // Login, Google y registro (RF-90 b). Solo si va a emitir: User.StartSession(), revocación de todos los refresh
    // anteriores del usuario y el refresh nuevo con ese sid, en un SaveChanges junto con los cambios pendientes del
    // usuario (p. ej. la vinculación de Google). Ante un conflicto de concurrencia (dos logins, o un refresh de la sesión
    // vieja) toma los valores de la base y repite con el mismo sid (R-28).
    Task<AuthTokenDto> StartSessionAsync(User user, Guid? preferredOrgId, CancellationToken ct = default);

    // Refresh (RF-90 c): emite el par con sessionId, el SessionId del refresh presentado (nunca CurrentSessionId), sin
    // tocar la sesión vigente ni revocar otros tokens; también si cae en otra organización o en la sesión sin
    // organización. Persiste el refresh nuevo con un SaveChanges.
    Task<AuthTokenDto> ContinueSessionAsync(User user, Guid sessionId, Guid? preferredOrgId, CancellationToken ct = default);
}

// El usuario no tiene ninguna membresía vigente desde la que emitir sesión.
public sealed class NoActiveMembershipException(Guid userId)
    : Exception($"User {userId} has no active organization membership.")
{
    public Guid UserId { get; } = userId;
}
