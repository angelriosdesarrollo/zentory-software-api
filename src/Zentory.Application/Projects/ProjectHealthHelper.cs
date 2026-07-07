namespace Zentory.Application.Projects;

/// <summary>
/// Computes project health (progress %, health score, health status, actionable alert) from
/// three weighted factors: financiero (pagos recibidos vs. tiempo transcurrido), tiempo (tareas
/// completadas vs. tiempo transcurrido) y horas (horas usadas vs. presupuestadas). Single source
/// of truth — usado por GetProjectByIdQuery y GetProjectsQuery. Pesos y tasa de penalización son
/// constantes ajustables; es un heurístico de primera versión que debe validarse con datos reales
/// (ver docs/HEALTH_SCORE.md y AGENT_AI.md TASK-AI-002), no una fórmula cerrada.
/// </summary>
internal static class ProjectHealthHelper
{
    private const decimal FinancialWeight = 0.40m;
    private const decimal TimeWeight      = 0.35m;
    private const decimal HoursWeight     = 0.25m;

    // Cuánto resta al score (0-100) cada punto porcentual de "atraso" — tiempo transcurrido por
    // encima de lo pagado o de lo avanzado. 250 => 20pp de atraso = 50 puntos menos.
    private const decimal GapPenaltyPerPoint = 250m;

    internal readonly record struct HealthInput(
        int       HoursUsed,
        int       HoursTotal,
        decimal   ContractValue,
        decimal   AmountPaid,
        DateTime? StartDate,
        DateTime? EndDate,
        int       TasksTotal,
        int       TasksDone);

    internal readonly record struct HealthResult(int Progress, int HealthScore, string HealthStatus, string? Alert);

    internal static HealthResult Compute(HealthInput input)
    {
        var hoursPct       = input.HoursTotal > 0 ? (decimal)input.HoursUsed / input.HoursTotal : (decimal?)null;
        var paidPct        = input.ContractValue > 0 ? input.AmountPaid / input.ContractValue : (decimal?)null;
        var timeElapsedPct = ComputeTimeElapsedPct(input.StartDate, input.EndDate);
        var taskDonePct    = input.TasksTotal > 0 ? (decimal)input.TasksDone / input.TasksTotal : (decimal?)null;

        var hoursScore     = ScoreFromUsage(hoursPct);
        var financialScore = ScoreFromGap(timeElapsedPct, paidPct);
        // Sin tablero de tareas, usa el avance por horas como proxy del avance real.
        var timeScore      = ScoreFromGap(timeElapsedPct, taskDonePct ?? hoursPct);

        var healthScore = Math.Clamp(
            (int)Math.Round(financialScore * FinancialWeight + timeScore * TimeWeight + hoursScore * HoursWeight),
            0, 100);

        var healthStatus = healthScore switch
        {
            >= 71 => "green",
            >= 41 => "yellow",
            _     => "red"
        };

        var progress = taskDonePct.HasValue
            ? (int)Math.Round(taskDonePct.Value * 100)
            : hoursPct.HasValue
                ? (int)Math.Round(hoursPct.Value * 100)
                : 0;

        var alert = BuildAlert(healthStatus, hoursPct, paidPct, timeElapsedPct, taskDonePct);

        return new HealthResult(progress, healthScore, healthStatus, alert);
    }

    private static decimal? ComputeTimeElapsedPct(DateTime? start, DateTime? end)
    {
        if (start is null || end is null || end <= start) return null;
        var totalDays   = (end.Value - start.Value).TotalDays;
        var elapsedDays = (DateTime.UtcNow - start.Value).TotalDays;
        return (decimal)Math.Clamp(elapsedDays / totalDays, 0, 2);
    }

    // Horas: mismas franjas que la versión anterior (single-factor), expresadas ahora 0-100.
    private static decimal ScoreFromUsage(decimal? usagePct)
    {
        if (usagePct is null) return 100m;
        var u = usagePct.Value;
        if (u <= 0.85m) return 100m;
        if (u <= 1m)    return 100m - (u - 0.7m) * 200m;
        return Math.Max(0m, 100m - (u - 1m) * 100m);
    }

    // Financiero / tiempo: penaliza cuando el tiempo transcurrido supera lo pagado o lo avanzado.
    private static decimal ScoreFromGap(decimal? timeElapsedPct, decimal? actualPct)
    {
        if (timeElapsedPct is null || actualPct is null) return 100m;
        var gap = Math.Max(0m, timeElapsedPct.Value - actualPct.Value);
        return Math.Clamp(100m - gap * GapPenaltyPerPoint, 0m, 100m);
    }

    private static string? BuildAlert(
        string status, decimal? hoursPct, decimal? paidPct, decimal? timeElapsedPct, decimal? taskDonePct)
    {
        if (status == "green") return null;

        if (timeElapsedPct is decimal elapsed1 && paidPct is decimal paid && elapsed1 - paid >= 0.20m)
            return $"Pagos atrasados: {Math.Round(paid * 100)}% cobrado con {Math.Round(elapsed1 * 100)}% del tiempo transcurrido.";

        if (timeElapsedPct is decimal elapsed2 && taskDonePct is decimal done && elapsed2 - done >= 0.20m)
            return $"Retraso de cronograma: {Math.Round(done * 100)}% de tareas completas con {Math.Round(elapsed2 * 100)}% del tiempo transcurrido.";

        if (hoursPct is decimal hours && hours > 0.85m)
            return $"Presupuesto de horas: {Math.Round(hours * 100)}% consumido.";

        return null;
    }
}
