using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record AcceptProposalByTokenCommand(Guid PublicToken) : IRequest;

public sealed class AcceptProposalByTokenCommandHandler : IRequestHandler<AcceptProposalByTokenCommand>
{
    private readonly IProposalRepository _proposals;
    private readonly IUnitOfWork         _uow;

    public AcceptProposalByTokenCommandHandler(IProposalRepository proposals, IUnitOfWork uow)
    {
        _proposals = proposals;
        _uow       = uow;
    }

    public async Task Handle(AcceptProposalByTokenCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByPublicTokenAsync(request.PublicToken, cancellationToken);

        if (proposal is null)
            throw new NotFoundException("Proposal", request.PublicToken);

        if (proposal.Status is "accepted" or "rejected")
            throw new ConflictException("PROPOSAL_CLOSED", "Esta propuesta ya fue respondida.");

        proposal.MarkAsAccepted();
        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
