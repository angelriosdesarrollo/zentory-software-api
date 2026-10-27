using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zentory.Application.Common.Security;

namespace Zentory.Infrastructure.Persistence.Seeding;

// Claves de la contraseña demo. Públicas para que los tests no repitan el literal.
public static class DemoDataSettings
{
    public const string PasswordKey                 = "DemoData:Password";
    public const string PasswordEnvironmentVariable = "DemoData__Password";
}

// Dónde va a escribir la siembra demo. Local = Development contra InMemory o contra un host loopback (RF-4, RF-57).
public sealed record DemoSeedTarget(string EnvironmentName, bool IsInMemory, bool IsLoopbackHost)
{
    public bool IsLocal
        => string.Equals(EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
           && (IsInMemory || IsLoopbackHost);
}

// Contraseña de las personas demo (PLAN-0018 RF-57, D-37; SR-0018.44). Se resuelve antes de escribir cualquier fila
// demo. Ni el valor configurado ni el valor por defecto aparecen en mensajes de error ni en logs (RNF-2).
public static class DemoPasswordResolver
{
    // Valor por defecto solo para local, el mismo que usa el recorrido de Playwright. Vive aquí y en ENV_VARS.md
    // (T34), nunca en appsettings*.json.
    public const string LocalDefaultPassword = "Zentory2026*";

    public static string Resolve(IConfiguration configuration, DemoSeedTarget target, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(logger);

        var configured = configuration[DemoDataSettings.PasswordKey];

        if (string.IsNullOrEmpty(configured))
        {
            if (!target.IsLocal) throw Failure(MissingMessage);

            logger.LogInformation(
                "{Key} is not set: demo users get the documented local default password.", DemoDataSettings.PasswordKey);
            return LocalDefaultPassword;
        }

        if (!PasswordPolicy.IsSatisfiedBy(configured)) throw Failure(InvalidMessage);

        // Fuera de local, el valor documentado es tan público como no tener contraseña (SR-0018.44).
        if (!target.IsLocal && string.Equals(configured, LocalDefaultPassword, StringComparison.Ordinal))
            throw Failure(DefaultOutsideLocalMessage);

        return configured;
    }

    private static InvalidOperationException Failure(string message) => new(message);

    private static string MissingMessage =>
        $"{DemoDataSettings.PasswordKey} (environment variable {DemoDataSettings.PasswordEnvironmentVariable}) is " +
        "required to seed demo data outside a local Development database.";

    private static string InvalidMessage =>
        $"{DemoDataSettings.PasswordKey} does not meet the password policy: it must be {PasswordPolicy.Requirement}.";

    private static string DefaultOutsideLocalMessage =>
        $"{DemoDataSettings.PasswordKey} cannot be the documented local default outside a local Development " +
        "database: set a different value.";
}
