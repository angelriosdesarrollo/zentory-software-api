using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectTasks.Commands;

public record MoveProjectTaskCommand(
    Guid   TaskId,
    string NewStatus) : IRequest;

public sealed class MoveProjectTaskCommandHandler : IRequestHandler<MoveProjectTaskCommand>
{
    private readonly IProjectTaskRepository _tasks;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public MoveProjectTaskCommandHandler(
        IProjectTaskRepository tasks,
        IUnitOfWork            uow,
        ITenantContext         tenant)
    {
        _tasks  = tasks;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(MoveProjectTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || task.OrganizationId != _tenant.OrganizationId)
            throw new Exceptions.NotFoundException("ProjectTask", request.TaskId);

        task.Move(request.NewStatus);
        await _tasks.UpdateAsync(task, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
