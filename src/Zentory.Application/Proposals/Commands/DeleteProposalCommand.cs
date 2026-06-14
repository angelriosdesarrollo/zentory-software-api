using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record DeleteProposalCommand(Guid Id) : IRequest;

public sealed class DeleteProposalCommandHandler : IRequestHandler<DeleteProposalCommand>
{
    private readonly IProposalRepository _proposals;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public DeleteProposalCommandHandler(
        IProposalRepository proposals,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _proposals   = proposals;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task Handle(DeleteProposalCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdAsync(request.Id, cancellationToken);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status == "accepted")
            throw new ConflictException("PROPOSAL_ACCEPTED", "No se puede eliminar una propuesta aceptada.");

        proposal.SoftDelete();

        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Proposal",
            entityId:   proposal.Id,
            action:     "Eliminó la propuesta",
            entityCode: proposal.Title,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
