using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record RejectProposalByTokenCommand(Guid PublicToken) : IRequest;

public sealed class RejectProposalByTokenCommandHandler : IRequestHandler<RejectProposalByTokenCommand>
{
    private readonly IProposalRepository _proposals;
    private readonly IUnitOfWork         _uow;
    private readonly IZentoryDbContext   _db;

    public RejectProposalByTokenCommandHandler(IProposalRepository proposals, IUnitOfWork uow, IZentoryDbContext db)
    {
        _proposals = proposals;
        _uow       = uow;
        _db        = db;
    }

    public async Task Handle(RejectProposalByTokenCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.FindLiveByPublicTokenAsync(_db, request.PublicToken, cancellationToken);

        if (proposal is null)
            throw new NotFoundException("Proposal", request.PublicToken);

        if (proposal.Status is "accepted" or "rejected")
            throw new ConflictException("PROPOSAL_CLOSED", "Esta propuesta ya fue respondida.");

        proposal.MarkAsRejected();
        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
