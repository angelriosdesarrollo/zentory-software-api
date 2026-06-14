using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Queries;

public record GetProjectByIdQuery(Guid Id) : IRequest<ProjectDto>;

public sealed class GetProjectByIdQueryHandler : IRequestHandler<GetProjectByIdQuery, ProjectDto>
{
    private readonly IProjectRepository _projects;
    private readonly IClientRepository  _clients;
    private readonly ITenantContext     _tenant;

    public GetProjectByIdQueryHandler(
        IProjectRepository projects,
        IClientRepository  clients,
        ITenantContext     tenant)
    {
        _projects = projects;
        _clients  = clients;
        _tenant   = tenant;
    }

    public async Task<ProjectDto> Handle(GetProjectByIdQuery request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.Id);

        var client = await _clients.GetByIdAsync(project.ClientId, cancellationToken);

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
