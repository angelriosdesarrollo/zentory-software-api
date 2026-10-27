using Zentory.Domain.Entities;

namespace Zentory.Application.Common.Interfaces;

public interface IJwtService
{
    // sessionId va en el claim sid (PLAN-0018 RF-90, D-47): el de la sesión que se inicia o se continúa, nunca
    // Guid.Empty. Lo lee SessionClaims.
    string GenerateAccessToken(
        User user, global::Zentory.Domain.Entities.Organization org, string activeOrgRole, string plan, Guid sessionId);

    // Access token sin organización activa (PLAN-0018 RF-85, D-44): solo identifica al usuario y su sesión.
    string GenerateAccessTokenWithoutOrganization(User user, Guid sessionId);

    string GenerateRefreshToken();

    // Vida del access token, leída de Jwt:AccessTokenExpiryMinutes (PLAN-0018 RF-31): la usan los handlers para expiresIn.
    TimeSpan AccessTokenLifetime { get; }
}
