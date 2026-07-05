using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectTasks.Commands;

public record UpdateProjectTaskCommand(
    Guid    TaskId,
    string  Title,
    string  Priority    = "medium",
    string? Description = null,
    string? DueDate     = null) : IRequest;

public sealed class UpdateProjectTaskCommandValidator : AbstractValidator<UpdateProjectTaskCommand>
{
    public UpdateProjectTaskCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
    }
}

public sealed class UpdateProjectTaskCommandHandler : IRequestHandler<UpdateProjectTaskCommand>
{
    private readonly IProjectTaskRepository _tasks;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public UpdateProjectTaskCommandHandler(IProjectTaskRepository tasks, IUnitOfWork uow, ITenantContext tenant)
    {
        _tasks  = tasks;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(UpdateProjectTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || task.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("ProjectTask", request.TaskId);

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(request.DueDate) && DateOnly.TryParse(request.DueDate, out var parsed))
            dueDate = parsed;

        task.Update(request.Title, request.Priority, request.Description, dueDate);

        await _tasks.UpdateAsync(task, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
