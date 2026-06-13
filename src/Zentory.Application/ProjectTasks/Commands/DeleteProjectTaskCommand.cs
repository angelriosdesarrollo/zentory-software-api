using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectTasks.Commands;

public record DeleteProjectTaskCommand(Guid TaskId) : IRequest;

public sealed class DeleteProjectTaskCommandHandler : IRequestHandler<DeleteProjectTaskCommand>
{
    private readonly IProjectTaskRepository _tasks;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public DeleteProjectTaskCommandHandler(
        IProjectTaskRepository tasks,
        IUnitOfWork            uow,
        ITenantContext         tenant)
    {
        _tasks  = tasks;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(DeleteProjectTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || task.OrganizationId != _tenant.OrganizationId)
            throw new Exceptions.NotFoundException("ProjectTask", request.TaskId);

        task.SoftDelete();
        await _tasks.UpdateAsync(task, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
