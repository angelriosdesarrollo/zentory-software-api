using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record CreateProjectDeliverableCommand(
    Guid    ProjectId,
    string  Name,
    string  Type,
    Guid?   MilestoneId = null,
    string? DueDate     = null) : IRequest<ProjectDeliverableDto>;

public sealed class CreateProjectDeliverableCommandValidator : AbstractValidator<CreateProjectDeliverableCommand>
{
    public CreateProjectDeliverableCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Type).NotEmpty().MaximumLength(50);
    }
}

public sealed class CreateProjectDeliverableCommandHandler
    : IRequestHandler<CreateProjectDeliverableCommand, ProjectDeliverableDto>
{
    private readonly IZentoryDbContext  _db;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork        _uow;
    private readonly ITenantContext     _tenant;

    public CreateProjectDeliverableCommandHandler(
        IZentoryDbContext db, IProjectRepository projects, IUnitOfWork uow, ITenantContext tenant)
    {
        _db       = db;
        _projects = projects;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<ProjectDeliverableDto> Handle(
        CreateProjectDeliverableCommand request,
        CancellationToken               cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Project", request.ProjectId);

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(request.DueDate) && DateOnly.TryParse(request.DueDate, out var parsed))
            dueDate = parsed;

        var deliverable = ProjectDeliverable.Create(
            _tenant.OrganizationId,
            request.ProjectId,
            request.Name,
            request.Type,
            dueDate,
            request.MilestoneId);

        _db.ProjectDeliverables.Add(deliverable);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ProjectDeliverableDto(
            deliverable.Id,
            deliverable.Name,
            deliverable.Type,
            deliverable.MilestoneId,
            deliverable.Status,
            deliverable.DueDate.HasValue ? deliverable.DueDate.Value.ToString("yyyy-MM-dd") : null,
            deliverable.ApprovedBy);
    }
}
