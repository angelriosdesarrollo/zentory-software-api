using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class CollaboratorJwtService : ICollaboratorJwtService
{
    private const int SessionDays = 7;

    private readonly string _key;
    private readonly string _issuer;
    private readonly string _audience;

    public CollaboratorJwtService(IConfiguration configuration)
    {
        // Signing key separada del JWT de empresa (Jwt:Key) a propósito: así un token de
        // un mundo nunca puede validarse por accidente contra el esquema del otro, aunque
        // el claim token_type ya lo evitaría — la key separada es un candado adicional barato.
        _key      = configuration["Jwt:CollaboratorKey"] ?? throw new InvalidOperationException("Jwt:CollaboratorKey is required.");
        _issuer   = configuration["Jwt:Issuer"]           ?? throw new InvalidOperationException("Jwt:Issuer is required.");
        _audience = configuration["Jwt:Audience"]         ?? throw new InvalidOperationException("Jwt:Audience is required.");
    }

    public string GenerateSessionToken(
        string              email,
        IReadOnlyList<Guid> collaboratorIds,
        Guid                activeCollaboratorId,
        Guid                activeOrganizationId,
        string              activeOrganizationName)
    {
        // collaborator_ids como string separado por comas en vez de un claim JSON array —
        // evita la complejidad de configurar deserialización de claims tipo array en
        // ClaimsPrincipal; se parsea con Split(',') en CollaboratorPortalContext.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, email),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("collaborator_ids", string.Join(',', collaboratorIds)),
            new Claim("active_collaborator_id", activeCollaboratorId.ToString()),
            new Claim("active_org_id", activeOrganizationId.ToString()),
            new Claim("active_org_name", activeOrganizationName),
            new Claim("token_type", "collaborator_session"),
        };

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddDays(SessionDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
