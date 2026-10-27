using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Zentory.Domain.Entities;

namespace Zentory.Tests.Support;

// Access token del esquema de usuario firmado a mano con TestJwtSettings, para los casos que JwtService no emite: un
// active_org_id inválido o ausente (PLAN-0018 RF-14, SR-0018.6) y la sesión sin organización armada sin pasar por login
// (RF-85, D-44). Solo sub, email, iniciales y sid más el active_org_id pedido; los tokens de una organización real salen
// de TestTokens.
//
// El sid es el de la sesión vigente del usuario sembrado, la misma regla de TestTokens.SessionOf (RF-90, D-47, R-25):
// reutiliza la que tenga o le inicia una. WithoutSession firma el mismo token sin sid, el de antes de M2c (CA-133), y
// Raw firma sub, sid y active_org_id tal cual, también inválidos o ausentes (null), sin tocar la base.
public static class TestUserJwt
{
    public const string ActiveOrgClaim = "active_org_id";

    public static string Sign(IServiceProvider services, User user, string? activeOrgId = null)
        => Raw(user.UserId.ToString(), new TestTokens(services).SessionOf(user).ToString("D"), activeOrgId, user.Email);

    public static string WithoutSession(User user, string? activeOrgId = null)
        => Raw(user.UserId.ToString(), sid: null, activeOrgId, user.Email);

    public static string Raw(string? sub, string? sid, string? activeOrgId, string email = "raw@zentory.test")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Email, email),
            new("initials", "PR"),
        };
        if (sub is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, sub));
        if (sid is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sid, sid));
        if (activeOrgId is not null)
            claims.Add(new Claim(ActiveOrgClaim, activeOrgId));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSettings.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:             TestJwtSettings.Issuer,
            audience:           TestJwtSettings.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
