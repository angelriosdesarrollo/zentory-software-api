using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record CreateProjectMilestoneCommand(
    Guid    ProjectId,
    string  Name,
    decimal Value,
    string? DueDate = null) : IRequest<ProjectMilestoneDto>;

public sealed class CreateProjectMilestoneCommandValidator : AbstractValidator<CreateProjectMilestoneCommand>
{
    public CreateProjectMilestoneCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Value).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateProjectMilestoneCommandHandler
    : IRequestHandler<CreateProjectMilestoneCommand, ProjectMilestoneDto>
{
    private readonly IZentoryDbContext   _db;
    private readonly IProjectRepository  _projects;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;

    public CreateProjectMilestoneCommandHandler(
        IZentoryDbContext  db,
        IProjectRepository projects,
        IUnitOfWork        uow,
        ITenantContext     tenant)
    {
        _db       = db;
        _projects = projects;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<ProjectMilestoneDto> Handle(
        CreateProjectMilestoneCommand request,
        CancellationToken             cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Project", request.ProjectId);

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(request.DueDate) && DateOnly.TryParse(request.DueDate, out var parsed))
            dueDate = parsed;

        var milestone = ProjectMilestone.Create(
            _tenant.OrganizationId,
            request.ProjectId,
            request.Name,
            request.Value,
            dueDate);

        _db.ProjectMilestones.Add(milestone);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ProjectMilestoneDto(
            milestone.Id,
            milestone.Name,
            milestone.Status,
            milestone.Value,
            milestone.DueDate.HasValue ? milestone.DueDate.Value.ToString("yyyy-MM-dd") : null);
    }
}
