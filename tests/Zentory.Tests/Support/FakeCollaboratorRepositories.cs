using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Tests.Support;

// Dobles en memoria escritos a mano (sin librería de mocks). Solo implementan lo que usan
// los handlers del portal; el resto lanza para que un uso nuevo no pase inadvertido.

public sealed class FakeCollaboratorRepository : ICollaboratorRepository
{
    public List<Collaborator> Items { get; } = [];
    public int ListByEmailCalls { get; private set; }

    public Task<Collaborator?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(c => c.Id == id));

    // Mismo filtro que CollaboratorRepository.ListByEmailAsync.
    public Task<IReadOnlyList<Collaborator>> ListByEmailAsync(string email, CancellationToken ct = default)
    {
        ListByEmailCalls++;
        var normalized = email.Trim().ToLower();
        IReadOnlyList<Collaborator> result = Items
            .Where(c => c.Email != null && c.Email.ToLower() == normalized
                && c.Status == "active" && c.DeletedAt == null)
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(Collaborator collaborator, CancellationToken ct = default)
    {
        Items.Add(collaborator);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Collaborator collaborator, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Collaborator>> ListAsync(
        Guid organizationId, string? search = null, string? status = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<int> CountActiveAsync(Guid organizationId, CancellationToken ct = default)
        => throw new NotSupportedException();
}

public sealed class FakeOrganizationRepository : IOrganizationRepository
{
    public List<Organization> Items { get; } = [];

    public Task<Organization?> GetByIdAsync(Guid organizationId, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(o => o.OrganizationId == organizationId));

    public Task AddAsync(Organization organization, CancellationToken ct = default)
    {
        Items.Add(organization);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Organization>> ListActiveByLegalTypeAsync(string legalType, CancellationToken ct = default)
        => throw new NotSupportedException();
}

public sealed class FakePilaVerificationRepository : IPilaVerificationRepository
{
    public List<PilaVerification> Items { get; } = [];

    public Task<PilaVerification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<PilaVerification?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(p => p.Token == token));

    public Task AddAsync(PilaVerification verification, CancellationToken ct = default)
    {
        Items.Add(verification);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PilaVerification verification, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PilaVerification?> GetByCollaboratorAndPeriodAsync(Guid collaboratorId, string period, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PilaVerification>> ListByOrganizationAndPeriodAsync(
        Guid organizationId, string period, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PilaVerification>> ListByCollaboratorAsync(Guid collaboratorId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<PilaVerificationDocument>> ListDocumentsAsync(Guid pilaVerificationId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<PilaVerificationDocument?> GetDocumentByIdAsync(Guid documentId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task AddDocumentAsync(PilaVerificationDocument document, CancellationToken ct = default)
        => throw new NotSupportedException();
}

public sealed class FakeCollaboratorPayoutInvoiceRepository : ICollaboratorPayoutInvoiceRepository
{
    public List<CollaboratorPayoutInvoice> Items { get; } = [];

    public Task<CollaboratorPayoutInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

    public Task<CollaboratorPayoutInvoice?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(i => i.PublicToken == token));

    public Task AddAsync(CollaboratorPayoutInvoice invoice, CancellationToken ct = default)
    {
        Items.Add(invoice);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CollaboratorPayoutInvoice invoice, CancellationToken ct = default) => Task.CompletedTask;

    public Task<CollaboratorPayoutInvoice?> GetByCollaboratorAndPeriodAsync(
        Guid collaboratorId, string period, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<CollaboratorPayoutInvoice>> ListByCollaboratorAsync(Guid collaboratorId, CancellationToken ct = default)
        => throw new NotSupportedException();
}

// Delega en el DbContext de prueba cuando hay uno, para que los cambios del handler
// (p. ej. MarkUsed del magic link) queden guardados en la base InMemory.
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly DbContext? _db;

    public FakeUnitOfWork(DbContext? db = null) { _db = db; }

    public int SaveCalls { get; private set; }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return _db is null ? 0 : await _db.SaveChangesAsync(ct);
    }

    // Sin transacción, como InMemory: la operación corre tal cual.
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
        => operation(ct);
}
