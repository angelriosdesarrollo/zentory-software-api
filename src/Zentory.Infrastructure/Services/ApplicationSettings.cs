using Microsoft.Extensions.Configuration;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

// URL pública del frontend para los enlaces de los correos (PLAN-0018 RF-62, D-38). Sin dominio por defecto: fuera de
// Development, AppUrlSettingsValidation hace fallar el arranque si App:BaseUrl falta, así que el valor local solo llega
// a usarse en Development.
public sealed class ApplicationSettings : IApplicationSettings
{
    public const string BaseUrlKey                = "App:BaseUrl";
    public const string DevelopmentDefaultBaseUrl = "http://localhost:3000";

    public string BaseUrl { get; }

    public ApplicationSettings(IConfiguration configuration)
    {
        var configured = configuration[BaseUrlKey];
        BaseUrl = string.IsNullOrWhiteSpace(configured)
            ? DevelopmentDefaultBaseUrl
            : configured.Trim().TrimEnd('/');
    }
}
