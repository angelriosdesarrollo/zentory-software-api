using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.TimeEntries.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Queries;

public record GetTimeEntryByIdQuery(Guid Id) : IRequest<TimeEntryDto>;

public sealed class GetTimeEntryByIdQueryHandler : IRequestHandler<GetTimeEntryByIdQuery, TimeEntryDto>
{
    private readonly ITimeEntryRepository    _timeEntries;
    private readonly IProjectRepository      _projects;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext          _tenant;

    public GetTimeEntryByIdQueryHandler(
        ITimeEntryRepository    timeEntries,
        IProjectRepository      projects,
        ICollaboratorRepository collaborators,
        ITenantContext          tenant)
    {
        _timeEntries   = timeEntries;
        _projects      = projects;
        _collaborators = collaborators;
        _tenant        = tenant;
    }

    public async Task<TimeEntryDto> Handle(GetTimeEntryByIdQuery request, CancellationToken cancellationToken)
    {
        var entry = await _timeEntries.GetByIdAsync(request.Id, cancellationToken);
        if (entry is null || entry.OrganizationId != _tenant.OrganizationId || entry.DeletedAt.HasValue)
            throw new NotFoundException("TimeEntry", request.Id);

        var project = await _projects.GetByIdAsync(entry.ProjectId, cancellationToken);

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
