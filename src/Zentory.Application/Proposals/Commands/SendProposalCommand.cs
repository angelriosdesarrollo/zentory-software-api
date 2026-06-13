using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record SendProposalCommand(Guid Id) : IRequest;

public sealed class SendProposalCommandHandler : IRequestHandler<SendProposalCommand>
{
    private readonly IProposalRepository _proposals;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;

    public SendProposalCommandHandler(IProposalRepository proposals, IUnitOfWork uow, ITenantContext tenant)
    {
        _proposals = proposals;
        _uow       = uow;
        _tenant    = tenant;
    }

    public async Task Handle(SendProposalCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdAsync(request.Id, cancellationToken);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status != "draft")
            throw new ConflictException("PROPOSAL_NOT_DRAFT", "Solo las propuestas en borrador pueden enviarse.");

        proposal.MarkAsSent();

        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
