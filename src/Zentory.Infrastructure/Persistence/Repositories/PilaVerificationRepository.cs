using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class PilaVerificationRepository : IPilaVerificationRepository
{
    private readonly ZentoryDbContext _db;

    public PilaVerificationRepository(ZentoryDbContext db) => _db = db;

    public async Task<PilaVerification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.PilaVerifications.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<PilaVerification?> GetByCollaboratorAndPeriodAsync(
        Guid collaboratorId, string period, CancellationToken ct = default)
        => await _db.PilaVerifications
            .FirstOrDefaultAsync(p => p.CollaboratorId == collaboratorId && p.Period == period, ct);

    public async Task<PilaVerification?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => await _db.PilaVerifications.FirstOrDefaultAsync(p => p.Token == token, ct);

    public async Task<IReadOnlyList<PilaVerification>> ListByOrganizationAndPeriodAsync(
        Guid organizationId, string period, CancellationToken ct = default)
        => await _db.PilaVerifications
            .Where(p => p.OrganizationId == organizationId && p.Period == period)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PilaVerification>> ListByCollaboratorAsync(
        Guid collaboratorId, CancellationToken ct = default)
        => await _db.PilaVerifications
            .Where(p => p.CollaboratorId == collaboratorId)
            .OrderByDescending(p => p.Period)
            .ToListAsync(ct);

    public async Task AddAsync(PilaVerification verification, CancellationToken ct = default)
        => await _db.PilaVerifications.AddAsync(verification, ct);

    public Task UpdateAsync(PilaVerification verification, CancellationToken ct = default)
    {
        _db.PilaVerifications.Update(verification);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PilaVerificationDocument>> ListDocumentsAsync(
        Guid pilaVerificationId, CancellationToken ct = default)
        => await _db.PilaVerificationDocuments
            .Where(d => d.PilaVerificationId == pilaVerificationId)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(ct);

    public async Task<PilaVerificationDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken ct = default)
        => await _db.PilaVerificationDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);

    public async Task AddDocumentAsync(PilaVerificationDocument document, CancellationToken ct = default)
        => await _db.PilaVerificationDocuments.AddAsync(document, ct);
}
