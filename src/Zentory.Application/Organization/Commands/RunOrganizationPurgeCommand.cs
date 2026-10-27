using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zentory.Application.Common.Options;

namespace Zentory.Application.Organization.Commands;

// Job diario de purga física (PLAN-0018 RF-78, RF-79; ADR purga rev. 2.2 §4; SR-0018.27): por cada organización con
// DeletedAt <= ahora − Organizations:PurgeAfterDays (o con una purga ya iniciada que falló a mitad), marca el inicio con
// el sello de concurrencia y luego archiva, borra filas, borra archivos y cierra. Después borra el archivo legal vencido.
// Hangfire lo programa con RecurringJobIds.OrganizationPurge, solo con Hangfire:ServerEnabled.
public sealed record RunOrganizationPurgeCommand : IRequest<OrganizationPurgeRunResult>;

// Resumen de una corrida: qué organizaciones se cerraron, cuáles fallaron (se retoman en la siguiente corrida por su
// PurgeStartedAt) y cuántos documentos archivados vencidos se borraron. Sin nombres ni datos del tenant.
public sealed record OrganizationPurgeRunResult(
    IReadOnlyList<Guid> Purged,
    IReadOnlyList<Guid> Failed,
    int                 ExpiredArchivesDeleted);

// Lo que la purga hace contra la base y el almacenamiento (Infrastructure/Persistence/Purge/OrganizationDataPurger).
// Separado del handler para que el orden de los pasos quede en un solo lugar y los tests puedan detener el job
// entre el inicio y el borrado.
public interface IOrganizationDataPurger
{
    // Paso 1 (SR-0018.27 a): marca PurgeStartedAt en las vencidas (DeletedAt <= cutoff), cada una confirmada con su
    // ConcurrencyStamp antes de seguir; las que perdieron la carrera con una restauración se saltan. Devuelve las
    // marcadas ahora más las que ya tenían PurgeStartedAt (purgas a medias que se retoman).
    Task<IReadOnlyList<Guid>> StartDuePurgesAsync(DateTime cutoffUtc, DateTime nowUtc, CancellationToken ct = default);

    // Pasos 2 a 6 para una organización ya marcada: Guid válido, archivo legal, filas, archivos y cierre. Idempotente:
    // repetirlo solo encuentra lo que falte.
    Task<OrganizationPurgeReport> PurgeAsync(Guid organizationId, DateTime nowUtc, CancellationToken ct = default);

    // Archivo legal con RetainUntil vencido: borra el PDF y luego la fila (RF-79, "Acceso").
    Task<int> DeleteExpiredArchiveAsync(DateTime nowUtc, CancellationToken ct = default);
}

// Conteos de una purga (van al AuditLog de cierre): filas por tabla, archivos borrados y documentos archivados.
public sealed record OrganizationPurgeReport(
    Guid                                OrganizationId,
    IReadOnlyDictionary<string, int>    RowsByTable,
    int                                 FilesDeleted,
    int                                 DocumentsArchived);

public sealed class RunOrganizationPurgeCommandHandler
    : IRequestHandler<RunOrganizationPurgeCommand, OrganizationPurgeRunResult>
{
    private readonly IOrganizationDataPurger                     _purger;
    private readonly OrganizationsOptions                        _options;
    private readonly TimeProvider                                _time;
    private readonly ILogger<RunOrganizationPurgeCommandHandler> _logger;

    public RunOrganizationPurgeCommandHandler(
        IOrganizationDataPurger                     purger,
        IOptions<OrganizationsOptions>              options,
        TimeProvider                                time,
        ILogger<RunOrganizationPurgeCommandHandler> logger)
    {
        _purger  = purger;
        _options = options.Value;
        _time    = time;
        _logger  = logger;
    }

    public async Task<OrganizationPurgeRunResult> Handle(RunOrganizationPurgeCommand request, CancellationToken ct)
    {
        var now    = _time.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-_options.PurgeAfterDays);

        var purged = new List<Guid>();
        var failed = new List<Guid>();

        foreach (var organizationId in await _purger.StartDuePurgesAsync(cutoff, now, ct))
        {
            try
            {
                await _purger.PurgeAsync(organizationId, now, ct);
                purged.Add(organizationId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Una organización que falla no detiene a las demás: queda con PurgeStartedAt y la corrida siguiente la
                // termina (ADR purga §4, "Reanudación"). Solo el Id: el mensaje no lleva datos del tenant.
                _logger.LogError(exception,
                    "Organization purge failed for {OrganizationId}; it stays marked and the next run resumes it.",
                    organizationId);
                failed.Add(organizationId);
            }
        }

        var expired = await _purger.DeleteExpiredArchiveAsync(now, ct);

        return new OrganizationPurgeRunResult(purged, failed, expired);
    }
}
