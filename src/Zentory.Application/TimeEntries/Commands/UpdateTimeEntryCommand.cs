using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.TimeEntries.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Commands;

public record UpdateTimeEntryCommand(
    Guid      Id,
    DateOnly  Date,
    decimal   Hours,
    decimal   RateCost,
    bool      Billable,
    decimal?  RateBilled  = null,
    string?   Description = null) : IRequest<TimeEntryDto>;

public sealed class UpdateTimeEntryCommandValidator : AbstractValidator<UpdateTimeEntryCommand>
{
    public UpdateTimeEntryCommandValidator()
    {
        RuleFor(x => x.Hours).GreaterThan(0).LessThanOrEqualTo(24);
        RuleFor(x => x.RateCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RateBilled).GreaterThanOrEqualTo(0).When(x => x.RateBilled.HasValue);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
    }
}

public sealed class UpdateTimeEntryCommandHandler : IRequestHandler<UpdateTimeEntryCommand, TimeEntryDto>
{
    private readonly ITimeEntryRepository   _timeEntries;
    private readonly IProjectRepository     _projects;
    private readonly ICollaboratorRepository _collaborators;
    private readonly IUnitOfWork            _uow;
    private readonly ITenantContext         _tenant;

    public UpdateTimeEntryCommandHandler(
        ITimeEntryRepository    timeEntries,
        IProjectRepository      projects,
        ICollaboratorRepository collaborators,
        IUnitOfWork             uow,
        ITenantContext          tenant)
    {
        _timeEntries   = timeEntries;
        _projects      = projects;
        _collaborators = collaborators;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task<TimeEntryDto> Handle(UpdateTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await _timeEntries.GetByIdAsync(request.Id, cancellationToken);
        if (entry is null || entry.OrganizationId != _tenant.OrganizationId || entry.DeletedAt.HasValue)
            throw new NotFoundException("TimeEntry", request.Id);

        if (entry.Status == "billed")
            throw new ConflictException("TIME_ENTRY_BILLED", "No se puede editar una entrada ya facturada.");

        entry.Update(request.Description, request.Date, request.Hours, request.RateCost, request.RateBilled, request.Billable);

        await _timeEntries.UpdateAsync(entry, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var project      = await _projects.GetByIdAsync(entry.ProjectId, cancellationToken);
        string? collName = null;
        if (entry.CollaboratorId.HasValue)
        {
            var coll = await _collaborators.GetByIdAsync(entry.CollaboratorId.Value, cancellationToken);
            collName = coll?.Name;
        }

        return new TimeEntryDto(
            entry.Id,
            entry.ProjectId,
            project?.Name ?? string.Empty,
            entry.CollaboratorId,
            collName,
            entry.Description,
            entry.Date,
            entry.Hours,
            entry.RateCost,
            entry.RateBilled,
            entry.Billable,
            entry.Status,
            entry.Currency,
            entry.BilledAt,
            entry.CreatedAt,
            entry.UpdatedAt);
    }
}
