using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record SectionInput(
    string  SectionType,
    string? Title,
    string? Content,
    short   SortOrder,
    bool    IsVisible,
    bool    IsEncrypted);

public record SaveProposalSectionsCommand(
    Guid                        Id,
    IReadOnlyList<SectionInput> Sections) : IRequest;

public sealed class SaveProposalSectionsCommandValidator : AbstractValidator<SaveProposalSectionsCommand>
{
    private static readonly HashSet<string> AllowedTypes =
    [
        "summary", "deliverables", "methodology", "timeline", "about",
        "scope", "pricing", "conditions", "acceptance", "custom"
    ];

    public SaveProposalSectionsCommandValidator()
    {
        RuleFor(x => x.Sections).NotNull();
        RuleForEach(x => x.Sections).ChildRules(s =>
        {
            s.RuleFor(x => x.SectionType)
             .NotEmpty()
             .Must(t => AllowedTypes.Contains(t))
             .WithMessage("Tipo de sección inválido.");
        });
    }
}

public sealed class SaveProposalSectionsCommandHandler : IRequestHandler<SaveProposalSectionsCommand>
{
    private readonly IProposalRepository _proposals;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;

    public SaveProposalSectionsCommandHandler(
        IProposalRepository proposals,
        IUnitOfWork         uow,
        ITenantContext      tenant)
    {
        _proposals = proposals;
        _uow       = uow;
        _tenant    = tenant;
    }

    public async Task Handle(SaveProposalSectionsCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdAsync(request.Id, cancellationToken);

        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status is "accepted" or "rejected")
            throw new ConflictException("PROPOSAL_CLOSED", "No se puede editar una propuesta aceptada o rechazada.");

        var newSections = request.Sections
            .Select(s => ProposalSection.Create(
                organizationId: _tenant.OrganizationId,
                proposalId:     proposal.Id,
                sectionType:    s.SectionType,
                sortOrder:      s.SortOrder,
                title:          s.Title,
                content:        s.Content,
                isVisible:      s.IsVisible,
                isEncrypted:    s.IsEncrypted))
            .ToList();

        await _proposals.ReplaceSectionsAsync(proposal.Id, newSections, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
