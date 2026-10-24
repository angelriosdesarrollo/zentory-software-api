using Microsoft.Extensions.Configuration;

namespace Zentory.Infrastructure.Extensions;

// Única lectura de la configuración de base: AddInfrastructure y el fail-fast del host (capa API) deben decidir
// "¿InMemory?" con el mismo predicado, o el guard podría dejar pasar una configuración que AddInfrastructure sí trata
// como InMemory.
public static class DatabaseSettings
{
    public const string UseInMemoryKey      = "Database:UseInMemory";
    public const string InMemoryNameKey     = "Database:InMemoryName";
    public const string DefaultInMemoryName = "ZentoryDev";

    public static bool UseInMemory(IConfiguration configuration)
        => string.Equals(configuration[UseInMemoryKey], "true", StringComparison.OrdinalIgnoreCase);

    // Los tests pasan un nombre por instancia de host: EF InMemory comparte las bases del mismo nombre en el proceso.
    public static string InMemoryName(IConfiguration configuration)
    {
        var name = configuration[InMemoryNameKey];
        return string.IsNullOrWhiteSpace(name) ? DefaultInMemoryName : name;
    }
}
