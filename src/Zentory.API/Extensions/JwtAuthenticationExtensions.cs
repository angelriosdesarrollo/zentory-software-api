using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Zentory.API.Extensions;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required");
        var collaboratorJwtKey = configuration["Jwt:CollaboratorKey"]
            ?? throw new InvalidOperationException("Jwt:CollaboratorKey is required");

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

    private static TokenValidationParameters BuildValidationParameters(IConfiguration configuration, string signingKey) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = configuration["Jwt:Issuer"],
        ValidAudience = configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
    };
}
