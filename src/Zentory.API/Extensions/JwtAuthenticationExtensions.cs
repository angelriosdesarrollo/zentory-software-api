using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Zentory.API.Extensions;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Fail-fast antes de registrar los esquemas (PLAN-0018 RF-31): claves de 32 bytes o más y distintas, y la
        // expiración del access token en 1..15 minutos.
        JwtSettingsValidation.Validate(configuration);

        var jwtKey             = configuration[JwtSettingsValidation.KeySetting]!;
        var collaboratorJwtKey = configuration[JwtSettingsValidation.CollaboratorKeySetting]!;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = BuildValidationParameters(configuration, jwtKey);
            })
            // Esquema separado para la sesión del portal de colaboradores — signing key propia
            // (Jwt:CollaboratorKey) para que un JWT de un mundo nunca valide en el otro por
            // accidente, aunque el claim token_type/la policy ya lo evitarían igual.
            .AddJwtBearer("CollaboratorScheme", options =>
            {
                options.TokenValidationParameters = BuildValidationParameters(configuration, collaboratorJwtKey);
            });

        return services;
    }

    // ClockSkew explícito en cero en ambos esquemas (RF-31, SR-2.12): el valor por defecto de JwtBearer (5 min) dejaría
    // un access token de 15 min vivo hasta 20.
    private static TokenValidationParameters BuildValidationParameters(IConfiguration configuration, string signingKey) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero,
        ValidIssuer = configuration["Jwt:Issuer"],
        ValidAudience = configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
    };
}
