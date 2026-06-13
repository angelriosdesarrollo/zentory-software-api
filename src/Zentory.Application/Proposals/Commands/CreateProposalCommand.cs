using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record ProposalItemInput(
    string  Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct = 0,
    short   SortOrder   = 0);

public record CreateProposalCommand(
    Guid                           ClientId,
    string                         Title,
    IReadOnlyList<ProposalItemInput> Items,
    string                         Currency   = "COP",
    string?                        IntroText  = null,
    DateTime?                      ExpiresAt  = null,
    Guid?                          TemplateId = null) : IRequest<Guid>;

public sealed class CreateProposalCommandValidator : AbstractValidator<CreateProposalCommand>
{
    public CreateProposalCommandValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Description).NotEmpty().MaximumLength(500);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.DiscountPct).InclusiveBetween(0, 100);
        });
    }
}

public sealed class CreateProposalCommandHandler : IRequestHandler<CreateProposalCommand, Guid>
{
    private readonly IProposalRepository _proposals;
    private readonly IClientRepository   _clients;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;

    public CreateProposalCommandHandler(
        IProposalRepository proposals,
        IClientRepository   clients,
        IUnitOfWork         uow,
        ITenantContext      tenant)
    {
        _proposals = proposals;
        _clients   = clients;
        _uow       = uow;
        _tenant    = tenant;
    }

    public async Task<Guid> Handle(CreateProposalCommand request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(request.ClientId, cancellationToken);
        if (client is null || client.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Client", request.ClientId);

        var proposal = Proposal.Create(
            _tenant.OrganizationId,
            request.ClientId,
            request.Title,
            request.Currency,
            request.TemplateId,
            createdBy: _tenant.UserId,
            expiresAt: request.ExpiresAt);

        if (request.IntroText is not null)
            proposal.UpdateContent(request.Title, request.IntroText, null);

        foreach (var (item, idx) in request.Items.Select((item, i) => (item, i)))
        {
            var proposalItem = ProposalItem.Create(
                _tenant.OrganizationId,
                proposal.Id,
                item.Description,
                item.Quantity,
                item.UnitPrice,
                (short)idx,
                item.DiscountPct);

            proposal.AddItem(proposalItem);
        }

        await _proposals.AddAsync(proposal, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return proposal.Id;
    }
}
