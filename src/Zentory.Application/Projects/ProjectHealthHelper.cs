namespace Zentory.Application.Projects;

/// <summary>
/// Computes project health (progress %, health score, health status) from hours consumed.
/// Single source of truth — used by project queries and profitability stats.
/// </summary>
internal static class ProjectHealthHelper
{
    /// <returns>(progress, healthScore, healthStatus) where healthStatus is "green" | "yellow" | "red"</returns>
    internal static (int Progress, int HealthScore, string HealthStatus) Compute(int hoursUsed, int hoursTotal)
    {
        var progress = hoursTotal > 0
            ? (int)Math.Round((decimal)hoursUsed / hoursTotal * 100)
            : 0;

        if (hoursTotal == 0)
            return (progress, 100, "green");

        var usagePct = (decimal)hoursUsed / hoursTotal;

        if (usagePct > 1m)
            return (progress, Math.Max(0, 100 - (int)((usagePct - 1m) * 100)), "red");

        if (usagePct > 0.85m)
            return (progress, 100 - (int)((usagePct - 0.7m) * 200), "yellow");

        return (progress, 100, "green");
    }
}
