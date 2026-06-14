using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Projects.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Queries;

public record GetProjectsQuery(
    string? Search   = null,
    string? Status   = null,
    Guid?   ClientId = null) : IRequest<IReadOnlyList<ProjectSummaryDto>>;

public sealed class GetProjectsQueryHandler
    : IRequestHandler<GetProjectsQuery, IReadOnlyList<ProjectSummaryDto>>
{
    private readonly IProjectRepository _projects;
    private readonly IClientRepository  _clients;
    private readonly ITenantContext     _tenant;

    public GetProjectsQueryHandler(
        IProjectRepository projects,
        IClientRepository  clients,
        ITenantContext     tenant)
    {
        _projects = projects;
        _clients  = clients;
        _tenant   = tenant;
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> Handle(
        GetProjectsQuery  request,
        CancellationToken cancellationToken)
    {
        var list = await _projects.ListAsync(
            _tenant.OrganizationId,
            request.Search,
            request.Status,
            request.ClientId,
            cancellationToken);

        var clientList = await _clients.ListAsync(_tenant.OrganizationId, ct: cancellationToken);
        var clientMap  = clientList.ToDictionary(c => c.Id, c => c.Name);

        return list.Select(p =>
        {
            var (progress, healthScore, healthStatus) = ProjectHealthHelper.Compute(p.HoursUsed, p.HoursTotal);
            return new ProjectSummaryDto(
                Id:           p.Id,
                Code:         $"PRJ-{p.Id.ToString("N")[..8].ToUpperInvariant()}",
                Name:         p.Name,
                ClientId:     p.ClientId,
                ClientName:   clientMap.GetValueOrDefault(p.ClientId, string.Empty),
                Status:       p.Status.ToString(),
                BillingType:  p.BillingType.ToString(),
                ContractValue: p.ContractValue,
                Currency:     p.Currency,
                HoursTotal:   p.HoursTotal,
                HoursUsed:    p.HoursUsed,
                Progress:     progress,
                HealthScore:  healthScore,
                HealthStatus: healthStatus,
                StartDate:    p.StartDate,
                EndDate:      p.EndDate);
        }).ToList();
    }
}
