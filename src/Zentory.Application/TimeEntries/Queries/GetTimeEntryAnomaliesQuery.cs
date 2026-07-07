using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Queries;

// Detección de horas atípicas por reglas simples (sin ML): ninguna de estas reglas requiere IA,
// son estadística básica sobre datos que ya existen. Solo informativo — no bloquea la aprobación
// (ver ApproveTimeEntryCommand, que no consulta esto).
public record GetTimeEntryAnomaliesQuery(
    Guid?     ProjectId = null,
    DateTime? From      = null,
    DateTime? To        = null) : IRequest<IReadOnlyList<TimeEntryAnomalyDto>>;

public record TimeEntryAnomalyDto(
    Guid     EntryId,
    Guid?    CollaboratorId,
    string?  CollaboratorName,
    DateOnly Date,
    decimal  Hours,
    string   Reason,
    string   Severity); // "info" | "warning"

public sealed class GetTimeEntryAnomaliesQueryHandler
    : IRequestHandler<GetTimeEntryAnomaliesQuery, IReadOnlyList<TimeEntryAnomalyDto>>
{
    // Cuántas desviaciones estándar sobre el promedio propio del colaborador se consideran atípicas.
    private const double ZScoreThreshold = 2.0;
    // Ventana histórica usada para calcular el promedio/desviación de cada colaborador cuando
    // el caller no especifica un rango — 8 semanas da suficiente muestra sin arrastrar datos viejos.
    private const int DefaultLookbackDays = 56;

    private readonly ITimeEntryRepository    _timeEntries;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext          _tenant;

    public GetTimeEntryAnomaliesQueryHandler(
        ITimeEntryRepository    timeEntries,
        ICollaboratorRepository collaborators,
        ITenantContext          tenant)
    {
        _timeEntries   = timeEntries;
        _collaborators = collaborators;
        _tenant        = tenant;
    }

    public async Task<IReadOnlyList<TimeEntryAnomalyDto>> Handle(
        GetTimeEntryAnomaliesQuery request, CancellationToken ct)
    {
        var from = request.From ?? DateTime.UtcNow.AddDays(-DefaultLookbackDays);
        var to   = request.To   ?? DateTime.UtcNow;

        var entries = await _timeEntries.ListAsync(
            _tenant.OrganizationId, from, to, status: null,
            projectId: request.ProjectId, collaboratorId: null, ct: ct);

        var collList = await _collaborators.ListAsync(_tenant.OrganizationId, ct: ct);
        var collMap  = collList.ToDictionary(c => c.Id, c => c.Name);

        var statsByCollaborator = entries
            .Where(e => e.CollaboratorId.HasValue)
            .GroupBy(e => e.CollaboratorId!.Value)
            .ToDictionary(g => g.Key, g =>
            {
                var hours  = g.Select(e => (double)e.Hours).ToList();
                var mean   = hours.Average();
                var stdDev = hours.Count > 1
                    ? Math.Sqrt(hours.Sum(h => Math.Pow(h - mean, 2)) / hours.Count)
                    : 0d;
                return (Mean: mean, StdDev: stdDev);
            });

        var anomalies = new List<TimeEntryAnomalyDto>();

        foreach (var entry in entries)
        {
            var collaboratorName = entry.CollaboratorId.HasValue
                ? collMap.GetValueOrDefault(entry.CollaboratorId.Value)
                : null;

            if (entry.CollaboratorId.HasValue
                && statsByCollaborator.TryGetValue(entry.CollaboratorId.Value, out var stats)
                && stats.StdDev > 0
                && ((double)entry.Hours - stats.Mean) / stats.StdDev > ZScoreThreshold)
            {
                anomalies.Add(new TimeEntryAnomalyDto(
                    entry.Id, entry.CollaboratorId, collaboratorName, entry.Date, entry.Hours,
                    $"{entry.Hours}h muy por encima del promedio del colaborador ({stats.Mean:F1}h/día).",
                    "warning"));
                continue; // ya está marcada; no se acumulan varios motivos sobre la misma entrada
            }

            if (string.IsNullOrWhiteSpace(entry.Description) && entry.Hours > 8)
            {
                anomalies.Add(new TimeEntryAnomalyDto(
                    entry.Id, entry.CollaboratorId, collaboratorName, entry.Date, entry.Hours,
                    "Más de 8 horas registradas sin descripción.", "warning"));
                continue;
            }

            if (entry.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                anomalies.Add(new TimeEntryAnomalyDto(
                    entry.Id, entry.CollaboratorId, collaboratorName, entry.Date, entry.Hours,
                    "Horas registradas en fin de semana.", "info"));
            }
        }

        return anomalies;
    }
}
