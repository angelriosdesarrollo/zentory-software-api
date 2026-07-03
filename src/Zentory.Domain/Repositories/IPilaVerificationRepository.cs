using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IPilaVerificationRepository
{
    Task<PilaVerification?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PilaVerification?> GetByCollaboratorAndPeriodAsync(Guid collaboratorId, string period, CancellationToken ct = default);
    Task<PilaVerification?> GetByTokenAsync(Guid token, CancellationToken ct = default);
    Task<IReadOnlyList<PilaVerification>> ListByOrganizationAndPeriodAsync(Guid organizationId, string period, CancellationToken ct = default);
    Task<IReadOnlyList<PilaVerification>> ListByCollaboratorAsync(Guid collaboratorId, CancellationToken ct = default);
    Task AddAsync(PilaVerification verification, CancellationToken ct = default);
    Task UpdateAsync(PilaVerification verification, CancellationToken ct = default);

    Task<IReadOnlyList<PilaVerificationDocument>> ListDocumentsAsync(Guid pilaVerificationId, CancellationToken ct = default);
    Task<PilaVerificationDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken ct = default);
    Task AddDocumentAsync(PilaVerificationDocument document, CancellationToken ct = default);
}
