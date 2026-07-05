using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;
using DomainValidationException = Zentory.Application.Exceptions.ValidationException;
using DomainValidationError     = Zentory.Application.Exceptions.ValidationError;
using NotFoundException         = Zentory.Application.Exceptions.NotFoundException;

namespace Zentory.Application.Projects.Commands;

public record ChangeProjectMilestoneStatusCommand(
    Guid   ProjectId,
    Guid   MilestoneId,
    string Status) : IRequest;
// "PENDING" | "IN_PROGRESS" | "DONE"

public sealed class ChangeProjectMilestoneStatusCommandValidator
    : AbstractValidator<ChangeProjectMilestoneStatusCommand>
{
    private static readonly string[] ValidStatuses = ["PENDING", "IN_PROGRESS", "DONE"];

    public ChangeProjectMilestoneStatusCommandValidator()
    {
        RuleFor(x => x.Status).Must(ValidStatuses.Contains)
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}.");
    }
}

public sealed class ChangeProjectMilestoneStatusCommandHandler
    : IRequestHandler<ChangeProjectMilestoneStatusCommand>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public ChangeProjectMilestoneStatusCommandHandler(
        IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(ChangeProjectMilestoneStatusCommand request, CancellationToken cancellationToken)
    {
        var milestone = await _db.ProjectMilestones.FirstOrDefaultAsync(
            m => m.Id == request.MilestoneId && m.ProjectId == request.ProjectId
              && m.OrganizationId == _tenant.OrganizationId,
            cancellationToken);

        if (milestone is null)
            throw new NotFoundException("ProjectMilestone", request.MilestoneId);

        if (request.Status == "DONE")
        {
            var hasPendingTasks = await _db.ProjectTasks.AnyAsync(
                t => t.MilestoneId == request.MilestoneId && t.DeletedAt == null && t.Status != "done",
                cancellationToken);

            if (hasPendingTasks)
                throw new DomainValidationException([
                    new DomainValidationError("status", "No se puede completar el hito mientras tenga tareas pendientes.")
                ]);
        }

        milestone.UpdateStatus(request.Status);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
