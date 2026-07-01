using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.ProjectTasks.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectTasks.Queries;

public record GetProjectTasksQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectTaskDto>>;

public sealed class GetProjectTasksQueryHandler
    : IRequestHandler<GetProjectTasksQuery, IReadOnlyList<ProjectTaskDto>>
{
    private readonly IProjectTaskRepository _tasks;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext _tenant;

    public GetProjectTasksQueryHandler(
        IProjectTaskRepository tasks,
        ICollaboratorRepository collaborators,
        ITenantContext tenant)
    {
        _tasks         = tasks;
        _collaborators = collaborators;
        _tenant        = tenant;
    }

    public async Task<IReadOnlyList<ProjectTaskDto>> Handle(
        GetProjectTasksQuery request,
        CancellationToken    cancellationToken)
    {
        var tasks = await _tasks.ListByProjectAsync(request.ProjectId, cancellationToken);

        var assigneeIds   = tasks.Where(t => t.AssigneeId.HasValue).Select(t => t.AssigneeId!.Value).Distinct().ToList();
        var collaborators = await _collaborators.ListAsync(_tenant.OrganizationId, ct: cancellationToken);
        var collabMap     = collaborators.ToDictionary(c => c.Id, c => c.Name);

        return tasks.Select(t => new ProjectTaskDto(
            t.Id,
            t.ProjectId,
            t.Title,
            t.Status,
            t.Priority,
            t.Description,
            t.AssigneeId,
            t.AssigneeId.HasValue ? collabMap.GetValueOrDefault(t.AssigneeId.Value) : null,
            t.DueDate?.ToString("yyyy-MM-dd"),
            t.CreatedAt,
            t.UpdatedAt,
            t.MilestoneId,
            t.StartDate?.ToString("yyyy-MM-dd"),
            t.Hours,
            t.Dependencies)).ToList();
    }
}
