using Microsoft.Extensions.Configuration;

namespace Zentory.Infrastructure.Extensions;

// Única lectura de Hangfire:ServerEnabled. El servidor (AddInfrastructure) y los jobs recurrentes
// (ScheduleRecurringJobs) se condicionan con la misma función: RecurringJob usa JobStorage.Current, y si uno se
// registrara sin el otro el job quedaría huérfano o fallaría al arrancar.
public static class HangfireSettings
{
    public const string ServerEnabledKey = "Hangfire:ServerEnabled";

    public static bool IsServerEnabled(IConfiguration configuration)
    {
        var value = configuration[ServerEnabledKey];
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (bool.TryParse(value, out var enabled)) return enabled;

        // Un valor inválido no se interpreta: apagar o encender jobs por un error de tipeo sería silencioso.
        throw new InvalidOperationException($"{ServerEnabledKey} must be 'true' or 'false'.");
    }
}
