using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectTasks.Commands;

public record CreateProjectTaskCommand(
    Guid     ProjectId,
    string   Title,
    string   Status      = "todo",
    string   Priority    = "medium",
    string?  Description = null,
    Guid?    AssigneeId  = null,
    string?  DueDate     = null) : IRequest<Guid>;

public sealed class CreateProjectTaskCommandHandler : IRequestHandler<CreateProjectTaskCommand, Guid>
{
    private readonly IProjectTaskRepository _tasks;
    private readonly IProjectRepository     _projects;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public CreateProjectTaskCommandHandler(
        IProjectTaskRepository tasks,
        IProjectRepository     projects,
        IUnitOfWork            uow,
        ITenantContext         tenant)
    {
        _tasks    = tasks;
        _projects = projects;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<Guid> Handle(CreateProjectTaskCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new Exceptions.NotFoundException("Project", request.ProjectId);

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(request.DueDate) && DateOnly.TryParse(request.DueDate, out var parsed))
            dueDate = parsed;

        var task = ProjectTask.Create(
            _tenant.OrganizationId,
            request.ProjectId,
            request.Title,
            request.Status,
            request.Priority,
            request.Description,
            request.AssigneeId,
            dueDate);

        await _tasks.AddAsync(task, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return task.Id;
    }
}
