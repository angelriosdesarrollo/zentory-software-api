using Hangfire;
using MediatR;
using Zentory.API.Middleware;
using Zentory.Application.PilaVerifications.Commands;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;

namespace Zentory.API.Extensions;

public static class WebApplicationExtensions
{
    private const string DevSeedEnabledKey           = "DevSeed:Enabled";
    private const string MonthlyPilaAutoRequestJobId = "monthly-pila-auto-request";

    public static async Task SeedDevDataAsync(this WebApplication app)
    {
        // Solo Development: InMemory ya no implica seed (un host Testing o desplegado no siembra usuarios de prueba).
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue(DevSeedEnabledKey, true)) return;

        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevDataSeeder>();
        await seeder.SeedAsync();
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
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    public static WebApplication ScheduleRecurringJobs(this WebApplication app)
    {
        // Sin servidor no hay quien ejecute el job: no se registra, y se avisa para que no pase inadvertido.
        if (!HangfireSettings.IsServerEnabled(app.Configuration))
        {
            app.Logger.LogWarning(
                "Hangfire job server is disabled ({Key}=false): recurring job {JobId} will not run.",
                HangfireSettings.ServerEnabledKey, MonthlyPilaAutoRequestJobId);
            return app;
        }

        // Job mensual: día 25 a las 06:00 UTC, crea solicitudes PILA del período
        // siguiente para las orgs empresa+studio que aún no las tienen (Fase E).
        RecurringJob.AddOrUpdate<IMediator>(
            MonthlyPilaAutoRequestJobId,
            mediator => mediator.Send(new RunMonthlyPilaAutoRequestCommand(), CancellationToken.None),
            "0 6 25 * *");

        return app;
    }
}
