using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record ItemInput(
    string  Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct = 0,
    short   SortOrder   = 0);

public record SaveProposalItemsCommand(
    Guid                     Id,
    IReadOnlyList<ItemInput> Items) : IRequest;

public sealed class SaveProposalItemsCommandValidator : AbstractValidator<SaveProposalItemsCommand>
{
    public SaveProposalItemsCommandValidator()
    {
        RuleFor(x => x.Items).NotNull();
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
            i.RuleFor(x => x.Quantity).GreaterThan(0);
            i.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
            i.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        });
    }
}

public sealed class SaveProposalItemsCommandHandler : IRequestHandler<SaveProposalItemsCommand>
{
    private readonly IProposalRepository _proposals;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;

    public SaveProposalItemsCommandHandler(
        IProposalRepository proposals,
        IUnitOfWork         uow,
        ITenantContext      tenant)
    {
        _proposals = proposals;
        _uow       = uow;
        _tenant    = tenant;
    }

    public async Task Handle(SaveProposalItemsCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdAsync(request.Id, cancellationToken);

        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status is "accepted" or "rejected")
            throw new ConflictException("PROPOSAL_CLOSED", "No se puede editar una propuesta aceptada o rechazada.");

        var newItems = request.Items
            .Select((item, i) => ProposalItem.Create(
                organizationId: _tenant.OrganizationId,
                proposalId:     proposal.Id,
                description:    item.Description,
                quantity:       item.Quantity,
                unitPrice:      item.UnitPrice,
                sortOrder:      item.SortOrder != 0 ? item.SortOrder : (short)i,
                discountPct:    item.DiscountPct))
            .ToList();

        await _proposals.ReplaceItemsAsync(proposal.Id, newItems, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
