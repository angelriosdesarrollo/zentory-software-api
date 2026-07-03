using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Services;

public sealed class JwtService : IJwtService
{
    private const int AccessTokenMinutes = 15;

    private readonly string _key;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration configuration)
    {
        _key      = configuration["Jwt:Key"]      ?? throw new InvalidOperationException("Jwt:Key is required.");
        _issuer   = configuration["Jwt:Issuer"]   ?? throw new InvalidOperationException("Jwt:Issuer is required.");
        _audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required.");
    }

    public string GenerateAccessToken(User user, Organization org, string activeOrgRole, string plan)
    {
        var initials = $"{user.FirstName[0]}{user.LastName[0]}".ToUpperInvariant();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("active_org_id",   org.OrganizationId.ToString()),
            new Claim("active_org_role", activeOrgRole),
            new Claim("plan",            plan),
            new Claim("legal_type",    org.LegalType),
            new Claim("role",            user.Role),
            new Claim("initials",        initials)
        };

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
