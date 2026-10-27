using System.Globalization;
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
    // Expiración del access token (PLAN-0018 RF-31, D-15; G-9): sale de la configuración, en 1..15 minutos. La valida
    // JwtSettingsValidation al arrancar; aquí se vuelve a comprobar para quien construya el servicio fuera del host.
    public const string AccessTokenExpiryMinutesSetting = "Jwt:AccessTokenExpiryMinutes";
    public const int    MinAccessTokenExpiryMinutes     = 1;
    public const int    MaxAccessTokenExpiryMinutes     = 15;

    private readonly string _key;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration configuration)
    {
        _key      = configuration["Jwt:Key"]      ?? throw new InvalidOperationException("Jwt:Key is required.");
        _issuer   = configuration["Jwt:Issuer"]   ?? throw new InvalidOperationException("Jwt:Issuer is required.");
        _audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required.");
        AccessTokenLifetime = ReadAccessTokenLifetime(configuration);
    }

    public TimeSpan AccessTokenLifetime { get; }

    // El mensaje nombra la clave y el rango, nunca el valor configurado.
    public static TimeSpan ReadAccessTokenLifetime(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!int.TryParse(configuration[AccessTokenExpiryMinutesSetting], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var minutes)
            || minutes < MinAccessTokenExpiryMinutes
            || minutes > MaxAccessTokenExpiryMinutes)
            throw new InvalidOperationException(
                $"{AccessTokenExpiryMinutesSetting} must be an integer between " +
                $"{MinAccessTokenExpiryMinutes} and {MaxAccessTokenExpiryMinutes}.");
        return TimeSpan.FromMinutes(minutes);
    }

    public string GenerateAccessToken(User user, Organization org, string activeOrgRole, string plan, Guid sessionId)
        => Write(
        [
            .. UserClaims(user, sessionId),
            new Claim("active_org_id",   org.OrganizationId.ToString()),
            new Claim("active_org_role", activeOrgRole),
            new Claim("plan",            plan),
            new Claim("legal_type",      org.LegalType),
        ]);

    // Sesión sin organización activa (PLAN-0018 RF-85, D-44): sin active_org_id, rol, plan ni legal_type. El middleware
    // de membresía la rechaza con 401 en toda ruta que no lleve [SkipMembershipValidation].
    public string GenerateAccessTokenWithoutOrganization(User user, Guid sessionId)
        => Write(UserClaims(user, sessionId));

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    // sid (PLAN-0018 RF-90, D-47): el Guid de la sesión en formato D. Un token de usuario sin sesión no se emite.
    private static List<Claim> UserClaims(User user, Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A user access token belongs to a session.", nameof(sessionId));

        var initials = $"{user.FirstName[0]}{user.LastName[0]}".ToUpperInvariant();
        return
        [
            new Claim(JwtRegisteredClaimNames.Sub,   user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Sid,   sessionId.ToString("D")),
            new Claim("role",                        user.Role),
            new Claim("initials",                    initials),
        ];
    }

    private string Write(IEnumerable<Claim> claims)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            expires:            DateTime.UtcNow.Add(AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
