using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Commands;

public record ConvertProposalToProjectCommand(
    Guid      Id,
    string    Name,
    string    BillingType,
    decimal   ContractValue,
    string    Currency  = "USD",
    int       HoursTotal = 0,
    DateTime? StartDate = null,
    DateTime? EndDate   = null) : IRequest<Guid>;

public sealed class ConvertProposalToProjectCommandValidator : AbstractValidator<ConvertProposalToProjectCommand>
{
    public ConvertProposalToProjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BillingType).NotEmpty()
            .Must(v => Enum.TryParse<BillingType>(v, ignoreCase: true, out _))
            .WithMessage("BillingType must be Hourly, Milestone or FixedPrice.");
        RuleFor(x => x.ContractValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HoursTotal).GreaterThanOrEqualTo(0);
    }
}

public sealed class ConvertProposalToProjectCommandHandler : IRequestHandler<ConvertProposalToProjectCommand, Guid>
{
    private readonly IProposalRepository _proposals;
    private readonly IProjectRepository  _projects;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public ConvertProposalToProjectCommandHandler(
        IProposalRepository proposals,
        IProjectRepository  projects,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _proposals   = proposals;
        _projects    = projects;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task<Guid> Handle(ConvertProposalToProjectCommand request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdAsync(request.Id, cancellationToken);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        if (proposal.Status != "accepted")
            throw new ConflictException("PROPOSAL_NOT_ACCEPTED",
                "Solo las propuestas aceptadas pueden convertirse en proyectos.");

        if (proposal.ConvertedToProjectId.HasValue)
            throw new ConflictException("ALREADY_CONVERTED",
                "Esta propuesta ya fue convertida en un proyecto.");

        var billingType = Enum.Parse<BillingType>(request.BillingType, ignoreCase: true);

        var project = Project.Create(
            _tenant.OrganizationId,
            proposal.ClientId,
            request.Name,
            billingType,
            request.ContractValue,
            request.Currency,
            request.HoursTotal,
            request.StartDate,
            request.EndDate,
            proposalId: proposal.Id);

        proposal.ConvertToProject(project.Id);

        await _projects.AddAsync(project, cancellationToken);
        await _proposals.UpdateAsync(proposal, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Proposal",
            entityId:   proposal.Id,
            action:     $"Convirtió la propuesta en proyecto \"{request.Name}\"",
            entityCode: proposal.Title,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return project.Id;
    }
}
