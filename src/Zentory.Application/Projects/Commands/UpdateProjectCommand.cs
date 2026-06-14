using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.DTOs;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record UpdateProjectCommand(
    Guid      Id,
    string    Name,
    string    BillingType,
    decimal   ContractValue,
    string    Currency,
    int       HoursTotal,
    DateTime? StartDate,
    DateTime? EndDate) : IRequest<ProjectDto>;

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BillingType).NotEmpty()
            .Must(v => Enum.TryParse<BillingType>(v, ignoreCase: true, out _))
            .WithMessage("BillingType must be Hourly, Milestone or FixedPrice.");
        RuleFor(x => x.ContractValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.HoursTotal).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateProjectCommandHandler : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
    private readonly IProjectRepository _projects;
    private readonly IClientRepository  _clients;
    private readonly IUnitOfWork        _uow;
    private readonly ITenantContext     _tenant;

    public UpdateProjectCommandHandler(
        IProjectRepository projects,
        IClientRepository  clients,
        IUnitOfWork        uow,
        ITenantContext     tenant)
    {
        _projects = projects;
        _clients  = clients;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.Id);

        var client = await _clients.GetByIdAsync(project.ClientId, cancellationToken);
        var billingType = Enum.Parse<BillingType>(request.BillingType, ignoreCase: true);

        project.Update(
            request.Name,
            billingType,
            request.ContractValue,
            request.Currency,
            request.HoursTotal,
            request.StartDate,
            request.EndDate);

        await _projects.UpdateAsync(project, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var (progress, healthScore, healthStatus) = ProjectHealthHelper.Compute(project.HoursUsed, project.HoursTotal);

        return new ProjectDto(
            Id:            project.Id,
            Code:          $"PRJ-{project.Id.ToString("N")[..8].ToUpperInvariant()}",
            Name:          project.Name,
            ClientId:      project.ClientId,
            ClientName:    client?.Name ?? string.Empty,
            Status:        project.Status.ToString(),
            BillingType:   project.BillingType.ToString(),
            ContractValue: project.ContractValue,
            Currency:      project.Currency,
            HoursTotal:    project.HoursTotal,
            HoursUsed:     project.HoursUsed,
            Progress:      progress,
            HealthScore:   healthScore,
            HealthStatus:  healthStatus,
            StartDate:     project.StartDate,
            EndDate:       project.EndDate,
            ProposalId:    project.ProposalId,
            CreatedAt:     project.CreatedAt,
            UpdatedAt:     project.UpdatedAt);
    }
}
