using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Invoice?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> ListAsync(Guid organizationId, string? status = null, Guid? clientId = null, CancellationToken ct = default);
    Task<int> CountThisMonthAsync(Guid organizationId, CancellationToken ct = default);
    /// <summary>Suma de AmountPaid por proyecto, para el factor financiero del Health Score. Solo incluye los proyectos con al menos una factura.</summary>
    Task<Dictionary<Guid, decimal>> GetAmountPaidByProjectIdsAsync(
        IEnumerable<Guid> projectIds, Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Invoice invoice, CancellationToken ct = default);
    Task UpdateAsync(Invoice invoice, CancellationToken ct = default);
}
