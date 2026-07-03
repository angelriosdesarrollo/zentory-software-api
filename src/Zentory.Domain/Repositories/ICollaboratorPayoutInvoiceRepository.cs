using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface ICollaboratorPayoutInvoiceRepository
{
    Task<CollaboratorPayoutInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CollaboratorPayoutInvoice?> GetByTokenAsync(Guid token, CancellationToken ct = default);
    Task<CollaboratorPayoutInvoice?> GetByCollaboratorAndPeriodAsync(Guid collaboratorId, string period, CancellationToken ct = default);
    Task<IReadOnlyList<CollaboratorPayoutInvoice>> ListByCollaboratorAsync(Guid collaboratorId, CancellationToken ct = default);
    Task AddAsync(CollaboratorPayoutInvoice invoice, CancellationToken ct = default);
    Task UpdateAsync(CollaboratorPayoutInvoice invoice, CancellationToken ct = default);
}
