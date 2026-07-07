using Zentory.Domain.Entities.Ai;

namespace Zentory.Domain.Repositories;

public interface IAiUsageLogRepository
{
    Task AddAsync(AiUsageLog log, CancellationToken ct = default);

    /// <summary>Cuenta de llamadas exitosas o fallidas del mes en curso — misma estrategia que
    /// IInvoiceRepository.CountThisMonthAsync: se cuenta directo sobre usage_logs, sin pre-agregar
    /// en ai.monthly_usage (esa tabla se difiere hasta que el volumen lo justifique).</summary>
    Task<int> CountThisMonthAsync(Guid organizationId, Guid featureId, CancellationToken ct = default);
}
