using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CashFlow.Commands;

public record DeleteCashFlowEntryCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteCashFlowEntryCommandHandler
    : IRequestHandler<DeleteCashFlowEntryCommand, Unit>
{
    private readonly ICashFlowEntryRepository _repo;
    private readonly IUnitOfWork              _uow;
    private readonly ITenantContext           _tenant;

    public DeleteCashFlowEntryCommandHandler(
        ICashFlowEntryRepository repo,
        IUnitOfWork              uow,
        ITenantContext           tenant)
    {
        _repo   = repo;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task<Unit> Handle(DeleteCashFlowEntryCommand request, CancellationToken ct)
    {
        var entry = await _repo.GetByIdAsync(request.Id, ct);
        if (entry is null || entry.OrganizationId != _tenant.OrganizationId || entry.DeletedAt != null)
            throw new NotFoundException("CashFlowEntry", request.Id);

        entry.SoftDelete();
        await _repo.UpdateAsync(entry, ct);
        await _uow.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
