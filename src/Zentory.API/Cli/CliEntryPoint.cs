using System.Collections;
using Npgsql;
using Zentory.API.Extensions;
using Zentory.Application.Extensions;
using Zentory.Infrastructure.Extensions;

namespace Zentory.API.Cli;

// Códigos de salida de la CLI (PLAN-0018 RF-35 d). Públicos para que los tests no repitan el número.
public static class CliExitCodes
{
    public const int Success      = 0;
    public const int Error        = 1;
    public const int Usage        = 2;
    public const int NotConfirmed = 3;
}

// Líneas de resultado de la CLI (RF-35 d): created, unchanged, replaced o error: <motivo>.
public static class CliResults
{
    public const string Created     = "created";
    public const string Unchanged   = "unchanged";
    public const string Replaced    = "replaced";
    public const string ErrorPrefix = "error: ";
    public const string TargetPrefix = "target: ";
    public const string YesFlag     = "--yes";
}

// Entrada y salida de una corrida de la CLI. Program.cs pasa la consola y el entorno del proceso; los tests, lectores,
// escritores y diccionarios propios, y ConfigureServices para reemplazar la base sin tocar el resto de la composición.
// IsInteractive dice si hay una terminal a la que pedirle el nombre de la base en db init --reset (RF-63 e).
public sealed record CliRuntime(
    TextReader Input,
    TextWriter Output,
    IReadOnlyDictionary<string, string?> EnvironmentVariables,
    Action<IServiceCollection>? ConfigureServices = null,
    bool IsInteractive = false)
{
    public static CliRuntime FromProcess()
        => new(Console.In, Console.Out, ProcessEnvironment(), IsInteractive: !Console.IsInputRedirected);

    private static Dictionary<string, string?> ProcessEnvironment()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            variables[(string)entry.Key] = entry.Value as string;
        return variables;
    }
}

// Punto de entrada de los verbos de CLI del host (PLAN-0018 RF-35, ADR-0002 anotado, SR-0018.1, SR-3.1, D-22).
// Program.cs lo llama antes de WebApplication.CreateBuilder: un verbo nunca construye el WebApplication, así que no hay
// Kestrel ni puerto HTTP, ni pipeline, ni migración o seed de arranque. Los servicios se componen a mano con
// Hangfire:ServerEnabled=false y la cadena de conexión sale solo de la variable de entorno.
public static class CliEntryPoint
{
    // Nombre de la variable de entorno de la cadena (SR-3.1). Es la forma de entorno de ConnectionStringKey.
    public static readonly string ConnectionStringVariable =
        DatabaseStartupSettings.ConnectionStringKey.Replace(":", "__", StringComparison.Ordinal);

    public static bool IsCliInvocation(string[] args)
        => args.Length > 0 && DatabaseStartupPolicy.IsCliVerb(args[0]);

    public static Task<int> RunAsync(string[] args) => RunAsync(args, CliRuntime.FromProcess());

    public static async Task<int> RunAsync(string[] args, CliRuntime runtime)
    {
        var output = runtime.Output;
        if (!IsCliInvocation(args))
            return Fail(output, CliExitCodes.Usage, $"expected a command: {SeedCli.Verb} or {DbCli.Verb}");

        // Solo la variable de entorno (SR-3.1): sin ella no hay respaldo en appsettings*.json ni en user-secrets.
        if (!runtime.EnvironmentVariables.TryGetValue(ConnectionStringVariable, out var connectionString)
            || string.IsNullOrWhiteSpace(connectionString))
            return Fail(output, CliExitCodes.Error,
                $"{ConnectionStringVariable} is not set; the CLI does not read appsettings*.json or user-secrets");

        var target = DescribeTarget(connectionString);
        if (target is null)
            return Fail(output, CliExitCodes.Error, $"{ConnectionStringVariable} is not a valid PostgreSQL connection string");

        output.WriteLine(CliResults.TargetPrefix + target);

        // db init (RF-63) evalúa sus guardas antes de componer los servicios: compone los suyos solo si pasan.
        if (string.Equals(args[0], DbCli.Verb, StringComparison.OrdinalIgnoreCase))
            return await DbCli.RunAsync(args, runtime, connectionString);

        await using var services = BuildServices(BuildConfiguration(runtime.EnvironmentVariables, connectionString),
            runtime.ConfigureServices);
        return await SeedCli.RunAsync(args, services, runtime.Input, output);
    }

    // "<host>/<database>" leído con NpgsqlConnectionStringBuilder: nunca incluye la contraseña ni el usuario. Null si la
    // cadena es ilegible (el mensaje de error no la repite).
    public static string? DescribeTarget(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.Host) ? null : $"{builder.Host}/{builder.Database}";
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Configuración de la CLI: las variables de entorno del proceso (claves con "__" como ":") y, como última fuente,
    // las que la CLI fija. Sin archivos ni user-secrets (SR-3.1, RF-35 e).
    public static IConfiguration BuildConfiguration(
        IReadOnlyDictionary<string, string?> environmentVariables, string connectionString)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(NormalizeVariables(environmentVariables))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseStartupSettings.ConnectionStringKey] = connectionString,
                [DatabaseSettings.UseInMemoryKey]             = "false",
                [HangfireSettings.ServerEnabledKey]           = "false",
            })
            .Build();

    // "A__B" y "A:B" (o la misma clave con otra capitalización) son la misma clave de configuración: se arma con el
    // indexador y gana la última, en vez de que AddInMemoryCollection lance por la clave repetida.
    private static Dictionary<string, string?> NormalizeVariables(IReadOnlyDictionary<string, string?> environmentVariables)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in environmentVariables)
            normalized[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        return normalized;
    }

    // Los servicios de la aplicación sin host web: misma composición de capas que Program.cs, sin Kestrel, sin pipeline y
    // sin servidor de Hangfire. Nada se arranca: los IHostedService registrados quedan sin iniciar. Sin ambiente,
    // AddInfrastructure trata la base como desplegada y Hangfire no prepara su esquema.
    public static ServiceProvider BuildServices(IConfiguration configuration, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddRequestContext();
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    // Confirmación interactiva (RF-35 f). Solo "y" o "yes" confirman; una línea vacía o el fin de la entrada no.
    public static async Task<bool> ConfirmAsync(TextReader input, TextWriter output, string question)
    {
        await output.WriteAsync($"{question} [y/N] ");
        await output.FlushAsync();
        var answer = (await input.ReadLineAsync())?.Trim();
        output.WriteLine();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static int Fail(TextWriter output, int exitCode, string reason)
    {
        output.WriteLine(CliResults.ErrorPrefix + reason);
        return exitCode;
    }
}
