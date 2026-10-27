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
    // InMemory arrancaría con datos efímeros y, en Development, con usuarios sembrados; y uno con PostgreSQL sin cadena
    // fallaría tarde y sin nombrar la clave.
    public static WebApplicationBuilder EnsureSafeDatabaseConfiguration(this WebApplicationBuilder builder)
    {
        if (!DatabaseSettings.UseInMemory(builder.Configuration))
        {
            // D-4: con PostgreSQL la cadena es obligatoria en cualquier ambiente. Sin ella, Npgsql y Hangfire fallarían
            // más tarde y con un error confuso. El mensaje nombra las claves, nunca valores (RF-5, RNF-2).
            if (string.IsNullOrWhiteSpace(
                    builder.Configuration.GetConnectionString(DatabaseStartupSettings.ConnectionStringName)))
                throw new InvalidOperationException(
                    $"{DatabaseStartupSettings.ConnectionStringKey} is required when {DatabaseSettings.UseInMemoryKey} " +
                    $"is false (current environment: {builder.Environment.EnvironmentName}). Set it with " +
                    "dotnet user-secrets, appsettings.Local.json or the ConnectionStrings__DefaultConnection " +
                    "environment variable.");

            return builder;
        }

        var environment = builder.Environment;
        if (environment.IsDevelopment() || environment.IsEnvironment(AppEnvironments.Testing)) return builder;

        // Sin valores de configuración en el mensaje: solo la clave y el nombre del ambiente.
        throw new InvalidOperationException(
            $"{DatabaseSettings.UseInMemoryKey}=true is only allowed in {Environments.Development} or " +
            $"{AppEnvironments.Testing} (current environment: {environment.EnvironmentName}).");
    }
}
