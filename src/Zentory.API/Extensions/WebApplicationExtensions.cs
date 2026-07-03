using Hangfire;
using MediatR;
using Zentory.API.Middleware;
using Zentory.Application.PilaVerifications.Commands;
using Zentory.Infrastructure.Persistence;

namespace Zentory.API.Extensions;

public static class WebApplicationExtensions
{
    public static async Task SeedDevDataAsync(this WebApplication app)
    {
        var useInMemory = app.Configuration.GetValue<bool>("Database:UseInMemory");
        if (!app.Environment.IsDevelopment() && !useInMemory) return;

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
        // Job mensual: día 25 a las 06:00 UTC, crea solicitudes PILA del período
        // siguiente para las orgs empresa+studio que aún no las tienen (Fase E).
        RecurringJob.AddOrUpdate<IMediator>(
            "monthly-pila-auto-request",
            mediator => mediator.Send(new RunMonthlyPilaAutoRequestCommand(), CancellationToken.None),
            "0 6 25 * *");

        return app;
    }
}
