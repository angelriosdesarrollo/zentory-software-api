using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record SendProposalCommand(
    Guid    Id,
    string? EmailTo = null,
    string? Message = null) : IRequest;

public sealed class SendProposalCommandHandler : IRequestHandler<SendProposalCommand>
{
    private readonly IProposalRepository    _proposals;
    private readonly IClientRepository      _clients;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;
    private readonly IEmailService          _email;
    private readonly IApplicationSettings   _settings;

    public SendProposalCommandHandler(
        IProposalRepository  proposals,
        IClientRepository    clients,
        IUnitOfWork          uow,
        ITenantContext       tenant,
        IEmailService        email,
        IApplicationSettings settings)
    {
        _proposals = proposals;
        _clients   = clients;
        _uow       = uow;
        _tenant    = tenant;
        _email     = email;
        _settings  = settings;
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

        if (!string.IsNullOrWhiteSpace(request.EmailTo))
        {
            var client = await _clients.GetByIdAsync(proposal.ClientId, cancellationToken);
            var url    = $"{_settings.BaseUrl}/proposal/{proposal.PublicToken}";
            await _email.SendProposalEmailAsync(
                request.EmailTo,
                proposal.Title,
                client?.Name ?? string.Empty,
                url,
                request.Message,
                cancellationToken);
        }
    }
}
