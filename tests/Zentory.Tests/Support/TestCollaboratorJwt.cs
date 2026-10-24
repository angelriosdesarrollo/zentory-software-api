using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// CollaboratorJwtService real con configuración en memoria, para que los tests lean los
// claims que de verdad se emiten y no los de un doble.
public static class TestCollaboratorJwt
{
    public static CollaboratorJwtService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:CollaboratorKey"] = "test-collaborator-signing-key-0123456789abcdef",
                ["Jwt:Issuer"]          = "zentory-tests",
                ["Jwt:Audience"]        = "zentory-tests",
            })
            .Build();
        return new CollaboratorJwtService(configuration);
    }

    public static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    public static string Claim(JwtSecurityToken token, string type)
        => token.Claims.Single(c => c.Type == type).Value;

    public static IReadOnlyList<Guid> CollaboratorIds(JwtSecurityToken token)
        => Claim(token, "collaborator_ids")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
}
