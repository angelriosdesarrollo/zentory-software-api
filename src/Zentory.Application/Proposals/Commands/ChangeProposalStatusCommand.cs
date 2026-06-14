using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record ChangeProposalStatusCommand(Guid Id, string Status) : IRequest;

public sealed class ChangeProposalStatusCommandValidator : AbstractValidator<ChangeProposalStatusCommand>
{
    private static readonly string[] ValidStatuses = ["draft", "sent", "viewed", "accepted", "rejected"];

    public ChangeProposalStatusCommandValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage("Estado inválido. Valores permitidos: draft, sent, viewed, accepted, rejected.");
    }
}

public sealed class ChangeProposalStatusCommandHandler : IRequestHandler<ChangeProposalStatusCommand>
{
    private readonly IProposalRepository  _proposals;
    private readonly IUnitOfWork          _uow;
    private readonly ITenantContext       _tenant;
    private readonly IActivityLogService  _activityLog;

    public ChangeProposalStatusCommandHandler(
        IProposalRepository  proposals,
        IUnitOfWork          uow,
        ITenantContext       tenant,
        IActivityLogService  activityLog)
    {
        _proposals   = proposals;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task Handle(ChangeProposalStatusCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdAsync(request.Id, cancellationToken);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status == request.Status)
            return;

        var from = proposal.Status;
        proposal.ForceStatus(request.Status);

        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Proposal",
            entityId:   proposal.Id,
            action:     $"Cambió estado de [{from}] a [{request.Status}]",
            entityCode: proposal.Title,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
