using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record ChangeProjectStatusCommand(Guid Id, string Status) : IRequest;

public sealed class ChangeProjectStatusCommandValidator : AbstractValidator<ChangeProjectStatusCommand>
{
    public ChangeProjectStatusCommandValidator()
    {
        RuleFor(x => x.Status).NotEmpty()
            .Must(v => Enum.TryParse<ProjectStatus>(v, ignoreCase: true, out _))
            .WithMessage("Status must be Active, Paused, Completed or Cancelled.");
    }
}

public sealed class ChangeProjectStatusCommandHandler : IRequestHandler<ChangeProjectStatusCommand>
{
    private readonly IProjectRepository  _projects;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public ChangeProjectStatusCommandHandler(
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

    public async Task Handle(ChangeProjectStatusCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId || project.IsDeleted)
            throw new NotFoundException("Project", request.Id);

        var from      = project.Status.ToString();
        var newStatus = Enum.Parse<ProjectStatus>(request.Status, ignoreCase: true);
        project.ChangeStatus(newStatus);

        await _projects.UpdateAsync(project, cancellationToken);
        await _activityLog.LogAsync(
            entityType: "Project",
            entityId:   project.Id,
            action:     $"Cambió estado de [{from}] a [{newStatus}]",
            entityCode: project.Name,
            ct:         cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
