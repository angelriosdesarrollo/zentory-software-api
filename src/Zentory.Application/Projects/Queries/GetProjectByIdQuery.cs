using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Queries;

public record GetProjectByIdQuery(Guid Id) : IRequest<ProjectDto>;

public sealed class GetProjectByIdQueryHandler : IRequestHandler<GetProjectByIdQuery, ProjectDto>
{
    private readonly IProjectRepository     _projects;
    private readonly IClientRepository      _clients;
    private readonly IInvoiceRepository     _invoices;
    private readonly IProjectTaskRepository _tasks;
    private readonly ITenantContext         _tenant;

    public GetProjectByIdQueryHandler(
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

    public async Task<ProjectDto> Handle(GetProjectByIdQuery request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.Id);

        var client = await _clients.GetByIdAsync(project.ClientId, cancellationToken);

        var amountPaidByProject = await _invoices.GetAmountPaidByProjectIdsAsync(
            [project.Id], _tenant.OrganizationId, cancellationToken);
        var taskCountsByProject = await _tasks.GetTaskCountsByProjectIdsAsync(
            [project.Id], _tenant.OrganizationId, cancellationToken);

        amountPaidByProject.TryGetValue(project.Id, out var amountPaid);
        taskCountsByProject.TryGetValue(project.Id, out var taskCounts);

        var health = ProjectHealthHelper.Compute(new ProjectHealthHelper.HealthInput(
            HoursUsed:     project.HoursUsed,
            HoursTotal:    project.HoursTotal,
            ContractValue: project.ContractValue,
            AmountPaid:    amountPaid,
            StartDate:     project.StartDate,
            EndDate:       project.EndDate,
            TasksTotal:    taskCounts.Total,
            TasksDone:     taskCounts.Done));

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
            Progress:      health.Progress,
            HealthScore:   health.HealthScore,
            HealthStatus:  health.HealthStatus,
            Alert:         health.Alert,
            StartDate:     project.StartDate,
            EndDate:       project.EndDate,
            ProposalId:    project.ProposalId,
            CreatedAt:     project.CreatedAt,
            UpdatedAt:     project.UpdatedAt,
            Type:          project.Type);
    }
}
