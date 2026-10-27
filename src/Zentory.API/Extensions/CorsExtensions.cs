namespace Zentory.API.Extensions;

public static class CorsExtensions
{
    public const string FrontendPolicy = "Frontend";

    // Orígenes del frontend validados al arrancar (PLAN-0018 RF-62, D-38). Sin AllowCredentials(): el token viaja en
    // Authorization y el frontend no usa credentials: "include", así que la respuesta nunca emite
    // Access-Control-Allow-Credentials (SR-0018.43).
    public static IServiceCollection AddFrontendCors(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = AppUrlSettingsValidation.Validate(configuration, environment);

        services.AddCors(options =>
        {
            options.AddPolicy(FrontendPolicy, policy =>
            {
                policy
                    .WithOrigins([.. settings.AllowedOrigins])
                    .AllowAnyHeader()
                    .AllowAnyMethod();

                // Solo Development: además de los configurados, http(s)://localhost en cualquier puerto.
                if (settings.AllowLocalhostOrigins)
                {
                    var configured = new HashSet<string>(settings.AllowedOrigins, StringComparer.OrdinalIgnoreCase);
                    policy.SetIsOriginAllowed(origin =>
                        configured.Contains(origin) || AppUrlSettingsValidation.IsLocalhostOrigin(origin));
                }
            });
        });

        return services;
    }
}
