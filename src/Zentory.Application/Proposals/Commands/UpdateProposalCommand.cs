using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Proposals.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record UpdateProposalCommand(
    Guid      Id,
    string    Title,
    string?   IntroText  = null,
    string?   Conditions = null,
    DateTime? ExpiresAt  = null) : IRequest<ProposalDto>;

public sealed class UpdateProposalCommandValidator : AbstractValidator<UpdateProposalCommand>
{
    public UpdateProposalCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
    }
}

public sealed class UpdateProposalCommandHandler : IRequestHandler<UpdateProposalCommand, ProposalDto>
{
    private readonly IProposalRepository _proposals;
    private readonly IClientRepository   _clients;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public UpdateProposalCommandHandler(
        IProposalRepository proposals,
        IClientRepository   clients,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _proposals   = proposals;
        _clients     = clients;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task<ProposalDto> Handle(UpdateProposalCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status is "accepted" or "rejected")
            throw new ConflictException("PROPOSAL_CLOSED", "No se puede editar una propuesta aceptada o rechazada.");

        var titleChanged = proposal.Title != request.Title;

        proposal.UpdateContent(request.Title, request.IntroText, request.Conditions);

        var action = titleChanged
            ? "Actualizó el título de la propuesta"
            : "Editó datos de la propuesta";

        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Proposal",
            entityId:   proposal.Id,
            action:     action,
            entityCode: proposal.Title,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var client   = await _clients.GetByIdAsync(proposal.ClientId, cancellationToken);
        var items    = proposal.Items.Select(i => new ProposalItemDto(
            i.Id, i.Description, i.Quantity, i.UnitPrice, i.DiscountPct, i.Total, i.SortOrder)).ToList();
        var sections = proposal.Sections.OrderBy(s => s.SortOrder).Select(s => new ProposalSectionDto(
            s.Id, s.SectionType, s.Title, s.Content, s.SortOrder, s.IsVisible, s.IsEncrypted, s.CreatedAt, s.UpdatedAt)).ToList();

        return new ProposalDto(
            proposal.Id,
            proposal.Title,
            proposal.ClientId,
            client?.Name ?? string.Empty,
            proposal.Status,
            proposal.TotalAmount,
            proposal.Currency,
            proposal.IntroText,
            proposal.ExpiresAt,
            proposal.SentAt,
            proposal.AcceptedAt,
            proposal.RejectedAt,
            proposal.ViewCount,
            proposal.ConvertedToProjectId,
            items,
            sections,
            proposal.CreatedAt,
            proposal.UpdatedAt);
    }
}
