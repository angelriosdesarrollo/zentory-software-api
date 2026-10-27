using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.API.Middleware;
using Zentory.Application.Organization.Commands;
using Zentory.Application.PilaVerifications.Commands;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;

namespace Zentory.API.Extensions;

// Clave de configuración del seed de desarrollo. Pública para que los tests no repitan el literal.
public static class DevSeedSettings
{
    public const string EnabledKey = "DevSeed:Enabled";
}

// Ids de los jobs recurrentes de Hangfire. Públicos para que los tests no repitan el literal.
public static class RecurringJobIds
{
    public const string MonthlyPilaAutoRequest = "monthly-pila-auto-request";

    // Purga física diaria de organizaciones eliminadas y del archivo legal vencido (PLAN-0018 RF-78, RF-79).
    public const string OrganizationPurge = "organization-purge";
}

// Expresiones cron (UTC) de los jobs recurrentes. Públicas para que los tests no repitan el literal.
public static class RecurringJobSchedules
{
    // Día 25 a las 06:00 UTC.
    public const string MonthlyPilaAutoRequest = "0 6 25 * *";

    // Todos los días a las 03:00 UTC (ADR purga §7).
    public const string OrganizationPurge = "0 3 * * *";
}

public static class WebApplicationExtensions
{
    // Arranque de base (PLAN-0018 RF-4, RF-5, RF-34; D-2): aplica DatabaseStartupPolicy. Migrate va antes de cualquier
    // seed; con InMemory no se migra, y fuera de Development o con un verbo de CLI no se hace nada.
    public static async Task InitializeDatabaseAsync(this WebApplication app, string[]? args = null)
    {
        var configuration = app.Configuration;
        var cliVerb = args is { Length: > 0 } && DatabaseStartupPolicy.IsCliVerb(args[0]) ? args[0] : null;
        var isRelational = !DatabaseSettings.UseInMemory(configuration);
        var host = isRelational
            ? DatabaseStartupPolicy.HostOf(configuration.GetConnectionString(DatabaseStartupSettings.ConnectionStringName))
            : null;
        var allowNonLocal = configuration.GetValue(DatabaseStartupSettings.AllowNonLocalDevelopmentKey, false);
        var devSeedEnabled = configuration.GetValue(DevSeedSettings.EnabledKey, true);

        DatabaseStartupDecision Decide(bool hasAppliedMigrations) => DatabaseStartupPolicy.Decide(
            app.Environment.EnvironmentName, isRelational, host, allowNonLocal, devSeedEnabled, cliVerb,
            hasAppliedMigrations);

        var decision = Decide(hasAppliedMigrations: true);
        if (decision.Fail is not null) throw new InvalidOperationException(decision.Fail);
        if (decision == DatabaseStartupDecision.Nothing) return;

        // La contraseña demo se resuelve antes de tocar la base: si falta o no cumple la política, nada se escribe
        // (RF-57, D-37).
        var demoPassword = decision.SeedDemo
            ? DemoPasswordResolver.Resolve(
                configuration,
                new DemoSeedTarget(app.Environment.EnvironmentName, !isRelational, DatabaseStartupPolicy.IsLoopbackHost(host)),
                app.Logger)
            : null;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();

        if (decision.Migrate)
        {
            var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
            await WithConnectionFailureTranslatedAsync(async () =>
                decision = await MigrateAndMarkAsync(
                    async () => (await db.Database.GetAppliedMigrationsAsync()).Any(),
                    () => db.Database.MigrateAsync(),
                    () => DatabaseMarkerWriter.EnsureLocalMarkerAsync(db, clock.GetUtcNow().UtcDateTime),
                    Decide));
        }

        // Catálogos primero (RF-33): la demo cuelga de los planes. La demo nunca va contra un host remoto (SR-0018.44).
        if (decision.SeedReference)
            await ActivatorUtilities.CreateInstance<ReferenceDataSeeder>(scope.ServiceProvider).SeedAsync();

        if (decision.SeedDemo)
            await scope.ServiceProvider.GetRequiredService<DevDataSeeder>().SeedAsync(demoPassword!);
    }

    // Paso Migrate del arranque (D-2, D-42). Las migraciones aplicadas se leen antes de migrar: solo una base sin ellas
    // recibe la marca de base local, y solo si la regla lo decide (loopback y sin override, SR-0018.33). La marca va
    // después de Migrate porque su tabla nace en M3a. Público para probar el orden sin PostgreSQL (CA-124).
    public static async Task<DatabaseStartupDecision> MigrateAndMarkAsync(
        Func<Task<bool>> hasAppliedMigrations,
        Func<Task> migrate,
        Func<Task<bool>> writeLocalMarker,
        Func<bool, DatabaseStartupDecision> decide)
    {
        var decision = decide(await hasAppliedMigrations());
        await migrate();
        if (decision.WriteLocalMarker) await writeLocalMarker();
        return decision;
    }

    private static async Task WithConnectionFailureTranslatedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (DatabaseStartupPolicy.ConnectionFailure(exception) is not null)
        {
            // Sin InnerException: el mensaje de Npgsql trae host y puerto (RF-5, RNF-2).
            throw DatabaseStartupPolicy.ConnectionFailure(exception)!;
        }
    }

    public static WebApplication UseAppMiddlewarePipeline(this WebApplication app)
    {
        app.UseMiddleware<GlobalExceptionHandlingMiddleware>(); // must be first

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseHangfireDashboard("/hangfire"); // solo dev — sin auth propia todavía
        }

        app.UseCors("Frontend");
        if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseAuthentication();
        // Después del routing implícito y de la autenticación, antes de la autorización: las policies de rol leen la
        // membresía que deja este middleware (PLAN-0018 RF-14, ADR-0006).
        app.UseMiddleware<MembershipValidationMiddleware>();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    public static WebApplication ScheduleRecurringJobs(this WebApplication app)
    {
        // Sin servidor no hay quien ejecute los jobs: no se registran, y se avisa para que no pase inadvertido.
        if (!HangfireSettings.IsServerEnabled(app.Configuration))
        {
            app.Logger.LogWarning(
                "Hangfire job server is disabled ({Key}=false): recurring jobs {JobIds} will not run.",
                HangfireSettings.ServerEnabledKey,
                string.Join(", ", RecurringJobIds.MonthlyPilaAutoRequest, RecurringJobIds.OrganizationPurge));
            return app;
        }

        // El gestor del contenedor escribe en el JobStorage del contenedor (el mismo que usan el servidor y los tests),
        // no en JobStorage.Current, que es global del proceso.
        var jobs = app.Services.GetRequiredService<IRecurringJobManager>();

        // Job mensual: crea solicitudes PILA del período siguiente para las orgs empresa+studio que aún no las tienen
        // (Fase E).
        jobs.AddOrUpdate<IMediator>(
            RecurringJobIds.MonthlyPilaAutoRequest,
            mediator => mediator.Send(new RunMonthlyPilaAutoRequestCommand(), CancellationToken.None),
            RecurringJobSchedules.MonthlyPilaAutoRequest);

        // Job diario: purga las organizaciones eliminadas hace más de Organizations:PurgeAfterDays y el archivo legal
        // vencido (PLAN-0018 RF-78, RF-79).
        jobs.AddOrUpdate<IMediator>(
            RecurringJobIds.OrganizationPurge,
            mediator => mediator.Send(new RunOrganizationPurgeCommand(), CancellationToken.None),
            RecurringJobSchedules.OrganizationPurge);

        return app;
    }
}
