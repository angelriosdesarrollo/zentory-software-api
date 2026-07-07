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
    private readonly IProjectRepository     _projects;
    private readonly IClientRepository      _clients;
    private readonly IInvoiceRepository     _invoices;
    private readonly IProjectTaskRepository _tasks;
    private readonly ITenantContext         _tenant;

    public GetProjectsQueryHandler(
        IProjectRepository     projects,
        IClientRepository      clients,
        IInvoiceRepository     invoices,
        IProjectTaskRepository tasks,
        ITenantContext         tenant)
    {
        _projects = projects;
        _clients  = clients;
        _invoices = invoices;
        _tasks    = tasks;
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

        var projectIds          = list.Select(p => p.Id).ToList();
        var amountPaidByProject = await _invoices.GetAmountPaidByProjectIdsAsync(
            projectIds, _tenant.OrganizationId, cancellationToken);
        var taskCountsByProject = await _tasks.GetTaskCountsByProjectIdsAsync(
            projectIds, _tenant.OrganizationId, cancellationToken);

        return list.Select(p =>
        {
            amountPaidByProject.TryGetValue(p.Id, out var amountPaid);
            taskCountsByProject.TryGetValue(p.Id, out var taskCounts);

            var health = ProjectHealthHelper.Compute(new ProjectHealthHelper.HealthInput(
                HoursUsed:     p.HoursUsed,
                HoursTotal:    p.HoursTotal,
                ContractValue: p.ContractValue,
                AmountPaid:    amountPaid,
                StartDate:     p.StartDate,
                EndDate:       p.EndDate,
                TasksTotal:    taskCounts.Total,
                TasksDone:     taskCounts.Done));

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
                Progress:     health.Progress,
                HealthScore:  health.HealthScore,
                HealthStatus: health.HealthStatus,
                Alert:        health.Alert,
                StartDate:    p.StartDate,
                EndDate:      p.EndDate,
                Type:         p.Type);
        }).ToList();
    }
}
