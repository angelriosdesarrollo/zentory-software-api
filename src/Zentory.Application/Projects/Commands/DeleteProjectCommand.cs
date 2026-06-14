using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record DeleteProjectCommand(Guid Id) : IRequest;

public sealed class DeleteProjectCommandHandler : IRequestHandler<DeleteProjectCommand>
{
    private readonly IProjectRepository  _projects;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public DeleteProjectCommandHandler(
        IProjectRepository  projects,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _projects    = projects;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.Id);

        project.SoftDelete();

        await _projects.UpdateAsync(project, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Project",
            entityId:   project.Id,
            action:     "Eliminó el proyecto",
            entityCode: project.Name,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
