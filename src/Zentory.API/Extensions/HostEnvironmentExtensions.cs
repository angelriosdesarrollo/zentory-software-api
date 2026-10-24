using Zentory.Infrastructure.Extensions;

namespace Zentory.API.Extensions;

// Ambiente del host de pruebas. Solo restringe (fail-fast de base, exclusión de appsettings.Local.json): ningún
// esquema de auth, policy, Swagger, dashboard ni herramienta de desarrollo debe depender de él (PLAN-0001, RF-23).
public static class AppEnvironments
{
    public const string Testing = "Testing";
}

public static class HostEnvironmentExtensions
{
    // Se llama antes de AddInfrastructure para que este sea el primer error del arranque: un host desplegado con
    // InMemory arrancaría con datos efímeros y, en Development, con usuarios sembrados.
    public static WebApplicationBuilder EnsureSafeDatabaseConfiguration(this WebApplicationBuilder builder)
    {
        if (!DatabaseSettings.UseInMemory(builder.Configuration)) return builder;

        var environment = builder.Environment;
        if (environment.IsDevelopment() || environment.IsEnvironment(AppEnvironments.Testing)) return builder;

        // Sin valores de configuración en el mensaje: solo la clave y el nombre del ambiente.
        throw new InvalidOperationException(
            $"{DatabaseSettings.UseInMemoryKey}=true is only allowed in {Environments.Development} or " +
            $"{AppEnvironments.Testing} (current environment: {environment.EnvironmentName}).");
    }
}
