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

        return new ProjectDto(
            project.Id,
            project.Name,
            project.ClientId,
            client?.Name ?? string.Empty,
            project.Status.ToString(),
            project.BillingType.ToString(),
            project.ContractValue,
            project.Currency,
            project.HoursTotal,
            project.HoursUsed,
            project.StartDate,
            project.EndDate,
            project.ProposalId,
            project.CreatedAt,
            project.UpdatedAt);
    }
}
