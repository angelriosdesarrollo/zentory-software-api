using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zentory.API.Extensions;
using Zentory.Application.Billing.Commands;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;

namespace Zentory.API.Cli;

// Verbo seed (PLAN-0018 RF-35, RF-36, RF-37; ADR-0002 §2, §3). Ejecuta solo el subcomando pedido: nunca migra ni corre
// los escenarios demo (SR-0018.1); la única vía de CLI que migra es db init (RF-63).
public static class SeedCli
{
    public const string Verb       = "seed";
    public const string Reference  = "reference";
    public const string AssignPlan = "assign-plan";

    public const string EmailOption    = "--email";
    public const string PlanOption     = "--plan";
    public const string OperatorOption = "--operator";
    public const string DryRunFlag     = "--dry-run";

    // Línea de --dry-run: lo que haría, sin escribir (RF-37, CA-42).
    public const string DryRunPrefix = "dry-run: ";
    public const string PlanPrefix   = "plan: ";

    private static readonly string[] ValueOptions = [EmailOption, PlanOption, OperatorOption];

    public static async Task<int> RunAsync(string[] args, IServiceProvider services, TextReader input, TextWriter output)
    {
        var arguments = args.Skip(1).ToList();
        var subcommand = arguments.FirstOrDefault();

        if (string.Equals(subcommand, Reference, StringComparison.OrdinalIgnoreCase))
            return await RunReferenceAsync(arguments.Skip(1).ToList(), services, input, output);
        if (string.Equals(subcommand, AssignPlan, StringComparison.OrdinalIgnoreCase))
            return await RunAssignPlanAsync(arguments.Skip(1).ToList(), services, input, output);

        return CliEntryPoint.Fail(output, CliExitCodes.Usage,
            $"usage: {Verb} {Reference} [{CliResults.YesFlag}] | {Verb} {AssignPlan} {EmailOption} <email> " +
            $"{PlanOption} <free|pro|studio> {OperatorOption} <name> [{DryRunFlag}] [{CliResults.YesFlag}]");
    }

    // ── seed reference (RF-36) ──────────────────────────────────────────────────────────────────────────────────

    private static async Task<int> RunReferenceAsync(
        List<string> options, IServiceProvider services, TextReader input, TextWriter output)
    {
        var unknown = options.Where(o => !string.Equals(o, CliResults.YesFlag, StringComparison.Ordinal)).ToList();
        if (unknown.Count > 0)
            return CliEntryPoint.Fail(output, CliExitCodes.Usage, $"unknown option(s): {string.Join(' ', unknown)}");

        var confirmed = options.Contains(CliResults.YesFlag, StringComparer.Ordinal)
            || await CliEntryPoint.ConfirmAsync(input, output, "Insert the missing reference data into this database?");
        if (!confirmed)
            return CliEntryPoint.Fail(output, CliExitCodes.NotConfirmed,
                $"not confirmed; nothing was written (pass {CliResults.YesFlag} to skip the prompt)");

        try
        {
            var inserted = await SeedReferenceAsync(services);
            output.WriteLine(inserted > 0 ? CliResults.Created : CliResults.Unchanged);
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            return CliEntryPoint.Fail(output, CliExitCodes.Error, Describe(exception, "reference seed"));
        }
    }

    // Filas insertadas por ReferenceDataSeeder. El seeder solo guarda cuando falta algo, así que la suma de lo guardado
    // distingue created de unchanged sin repetir la lista de catálogos.
    private static async Task<int> SeedReferenceAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();

        var saved = 0;
        void Count(object? sender, SavedChangesEventArgs e) => saved += e.EntitiesSavedCount;

        db.SavedChanges += Count;
        try
        {
            await ActivatorUtilities.CreateInstance<ReferenceDataSeeder>(scope.ServiceProvider).SeedAsync();
        }
        finally
        {
            db.SavedChanges -= Count;
        }

        return saved;
    }

    // ── seed assign-plan (RF-37) ────────────────────────────────────────────────────────────────────────────────

    // Orden: validación completa sin escribir (la misma corrida del comando en modo --dry-run), luego lo que hará,
    // luego la confirmación y solo entonces la escritura. Un rechazo o una falta de confirmación no cambian filas.
    private static async Task<int> RunAssignPlanAsync(
        List<string> options, IServiceProvider services, TextReader input, TextWriter output)
    {
        var parsed = ParseAssignPlan(options);
        if (parsed.Error is not null) return CliEntryPoint.Fail(output, CliExitCodes.Usage, parsed.Error);

        // Host y base para la auditoría (SR-0018.19), de la misma cadena que usa el DbContext y sin credenciales.
        var connectionString = services.GetRequiredService<IConfiguration>()
            .GetConnectionString(DatabaseStartupSettings.ConnectionStringName) ?? string.Empty;
        var target = CliEntryPoint.DescribeTarget(connectionString) ?? string.Empty;

        AssignPlanBySeedCommand Command(bool dryRun)
            => new(parsed.Email, parsed.Plan, parsed.Operator, target, dryRun);

        try
        {
            var preview = await SendAsync(services, Command(dryRun: true));
            if (!preview.Succeeded) return CliEntryPoint.Fail(output, CliExitCodes.Error, preview.Error!);

            output.WriteLine($"{PlanPrefix}{parsed.Email?.Trim()} {preview.PreviousPlan} -> {preview.NewPlan}");
            if (!preview.Changes)
            {
                output.WriteLine(CliResults.Unchanged);
                return CliExitCodes.Success;
            }

            if (parsed.DryRun)
            {
                output.WriteLine($"{DryRunPrefix}would be {ResultLine(preview.Outcome)} " +
                                 $"({preview.Outcome.ToString().ToLowerInvariant()}); nothing was written");
                return CliExitCodes.Success;
            }

            var confirmed = parsed.Yes
                || await CliEntryPoint.ConfirmAsync(input, output, "Change the plan of this owner?");
            if (!confirmed)
                return CliEntryPoint.Fail(output, CliExitCodes.NotConfirmed,
                    $"not confirmed; nothing was written (pass {CliResults.YesFlag} to skip the prompt)");

            var result = await SendAsync(services, Command(dryRun: false));
            if (!result.Succeeded) return CliEntryPoint.Fail(output, CliExitCodes.Error, result.Error!);

            output.WriteLine(ResultLine(result.Outcome));
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            return CliEntryPoint.Fail(output, CliExitCodes.Error, Describe(exception, "assign-plan"));
        }
    }

    // Cada corrida del comando en su propio scope: la escritura no reutiliza lo que la validación dejó en seguimiento.
    private static async Task<AssignPlanBySeedResult> SendAsync(IServiceProvider services, AssignPlanBySeedCommand command)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    // created, replaced (reasignación o bajada a free) o unchanged (RF-35 d).
    private static string ResultLine(AssignPlanOutcome outcome) => outcome switch
    {
        AssignPlanOutcome.Created                                 => CliResults.Created,
        AssignPlanOutcome.Replaced or AssignPlanOutcome.Downgraded => CliResults.Replaced,
        _                                                          => CliResults.Unchanged,
    };

    private sealed record AssignPlanArguments(
        string? Email, string? Plan, string? Operator, bool DryRun, bool Yes, string? Error = null);

    // Las opciones con valor no se repiten ni quedan sin valor; un valor faltante (p. ej. sin --operator) no es error
    // de uso: lo rechaza la validación del comando con su motivo.
    private static AssignPlanArguments ParseAssignPlan(List<string> options)
    {
        var values  = new Dictionary<string, string>(StringComparer.Ordinal);
        var dryRun  = false;
        var yes     = false;
        var unknown = new List<string>();

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            if (string.Equals(option, DryRunFlag, StringComparison.Ordinal)) { dryRun = true; continue; }
            if (string.Equals(option, CliResults.YesFlag, StringComparison.Ordinal)) { yes = true; continue; }

            if (!ValueOptions.Contains(option, StringComparer.Ordinal)) { unknown.Add(option); continue; }

            if (i + 1 >= options.Count || options[i + 1].StartsWith("--", StringComparison.Ordinal))
                return new(null, null, null, dryRun, yes, $"{option} needs a value");
            if (!values.TryAdd(option, options[++i]))
                return new(null, null, null, dryRun, yes, $"{option} was given more than once");
        }

        if (unknown.Count > 0)
            return new(null, null, null, dryRun, yes, $"unknown option(s): {string.Join(' ', unknown)}");

        return new(values.GetValueOrDefault(EmailOption), values.GetValueOrDefault(PlanOption),
            values.GetValueOrDefault(OperatorOption), dryRun, yes);
    }

    // ── Errores ─────────────────────────────────────────────────────────────────────────────────────────────────

    // Motivo sin valores de configuración (RNF-2): un fallo de conexión nombra la clave y el tipo, sin el mensaje de
    // Npgsql (trae host y puerto); un error de PostgreSQL, su SQLSTATE.
    // PostgresException va primero: es un NpgsqlException, pero la base sí respondió.
    private static string Describe(Exception exception, string operation)
    {
        // Otra operación cambió la suscripción a la vez (token de concurrencia o índice único de M3): sin efecto.
        const string concurrent = "the subscription changed while this ran; nothing was written, run it again";
        if (exception is DbUpdateConcurrencyException) return $"{operation} failed: {concurrent}";

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres.SqlState switch
                {
                    PostgresErrorCodes.UndefinedTable =>
                        $"{operation} failed: the schema is missing (SQLSTATE {postgres.SqlState}); run db init first",
                    PostgresErrorCodes.UniqueViolation => $"{operation} failed: {concurrent} (SQLSTATE {postgres.SqlState})",
                    _ => $"{operation} failed (SQLSTATE {postgres.SqlState})",
                };
        }

        if (DatabaseStartupPolicy.ConnectionFailure(exception) is { } connection) return connection.Message;

        return $"{operation} failed ({exception.GetType().Name})";
    }
}
