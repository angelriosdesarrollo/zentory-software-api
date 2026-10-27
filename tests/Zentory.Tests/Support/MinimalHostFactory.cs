using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Zentory.Tests.Support;

// Host mínimo sobre Program.cs para los tests que necesitan un ambiente distinto de Testing (Development, Staging,
// Production), donde ZentoryApiFactory no aplica. La raíz de contenido es una VersionedContentRoot, y toda la
// configuración restante llega por UseSetting, que el hosting mínimo ve como línea de comandos antes de
// AddInfrastructure.
//
// La configuración parte de NeutralizedMachineSettings(), TestJwtSettings.AsSettings() (PLAN-0018 RF-19 a) y
// TestAppUrlSettings.AsSettings() (RF-62); los ajustes del test van encima y pueden reemplazar cualquiera de ellos.
internal sealed class MinimalHostFactory : WebApplicationFactory<Program>
{
    // Claves que la máquina del desarrollador puede traer (variables de entorno, y en Development también
    // user-secrets) y que ningún host de pruebas debe usar. Se sobrescriben siempre, existan o no en el entorno.
    private static readonly string[] NeutralizedKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "R2:AccountId",
        "R2:AccessKey",
        "R2:SecretKey",
        "Resend:ApiKey",
        "Anthropic:ApiKey",
        "DemoData:Password",
        "App:BaseUrl",
        "Cors:AllowedOrigins",
    ];

    // Secciones completas: cualquier variable de entorno bajo ellas (p. ej. ConnectionStrings__Otra, Cors__Algo).
    private static readonly string[] NeutralizedSections = ["ConnectionStrings", "R2", "Cors"];

    // Prefijos de cadena de conexión que el proveedor de variables de entorno traduce a ConnectionStrings:<nombre>.
    private static readonly string[] ConnectionStringPrefixes =
        ["MYSQLCONNSTR_", "SQLAZURECONNSTR_", "SQLCONNSTR_", "CUSTOMCONNSTR_", "POSTGRESQLCONNSTR_"];

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly VersionedContentRoot _contentRoot = new();

    public MinimalHostFactory(string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _environment = environment;
        _settings = settings;
    }

    public string ContentRootPath => _contentRoot.Path;

    // Valores vacíos para las claves de la máquina: la línea de comandos gana sobre variables de entorno y
    // user-secrets, así el host nunca ve los valores reales. Solo se leen los nombres de las variables, nunca sus
    // valores.
    public static IReadOnlyDictionary<string, string?> NeutralizedMachineSettings()
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in NeutralizedKeys)
            settings[key] = string.Empty;

        foreach (var name in Environment.GetEnvironmentVariables().Keys.OfType<string>())
        {
            var key = ConfigurationKeyOf(name);
            if (key is not null)
                settings[key] = string.Empty;
        }

        return settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseContentRoot(_contentRoot.Path);

        var settings = new Dictionary<string, string?>(NeutralizedMachineSettings(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in TestJwtSettings.AsSettings())
            settings[key] = value;
        foreach (var (key, value) in TestAppUrlSettings.AsSettings())
            settings[key] = value;
        foreach (var (key, value) in _settings)
            settings[key] = value;

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _contentRoot.Dispose();
    }

    // Nombre de variable de entorno → clave de configuración neutralizada, o null si no hay que tocarla.
    private static string? ConfigurationKeyOf(string variableName)
    {
        foreach (var prefix in ConnectionStringPrefixes)
        {
            if (variableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && variableName.Length > prefix.Length)
                return "ConnectionStrings:" + variableName[prefix.Length..];
        }

        var key = variableName.Replace("__", ":", StringComparison.Ordinal);
        if (NeutralizedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            return key;

        return NeutralizedSections.Any(section => key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase))
            ? key
            : null;
    }
}
