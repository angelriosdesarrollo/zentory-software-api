using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectTasks.Commands;

public record UpdateProjectTaskGanttCommand(
    Guid      TaskId,
    Guid?     MilestoneId,
    string?   StartDate,
    string?   DueDate,
    int       Hours,
    string[]  Dependencies) : IRequest;

public sealed class UpdateProjectTaskGanttCommandHandler : IRequestHandler<UpdateProjectTaskGanttCommand>
{
    private readonly IProjectTaskRepository _tasks;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public UpdateProjectTaskGanttCommandHandler(
        IProjectTaskRepository tasks,
        IUnitOfWork            uow,
        ITenantContext         tenant)
    {
        _tasks  = tasks;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(UpdateProjectTaskGanttCommand request, CancellationToken cancellationToken)
    {
        // Nota: este comando también lo usa el tab "Tareas" (no solo Gantt) para vincular
        // una tarea a un hito y fijar horas estimadas — no gatear a Studio aquí bloquearía
        // esa funcionalidad básica para todos los planes. El gate de Gantt vive en el frontend
        // (página /projects/gantt), no en este comando compartido.
        var task = await _tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || task.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("ProjectTask", request.TaskId);

        DateOnly? startDate = null;
        if (!string.IsNullOrWhiteSpace(request.StartDate) && DateOnly.TryParse(request.StartDate, out var s))
            startDate = s;

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(request.DueDate) && DateOnly.TryParse(request.DueDate, out var d))
            dueDate = d;

        task.UpdateGantt(request.MilestoneId, startDate, dueDate, request.Hours, request.Dependencies);

        await _tasks.UpdateAsync(task, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
