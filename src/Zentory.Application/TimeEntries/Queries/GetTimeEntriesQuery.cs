using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.TimeEntries.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.TimeEntries.Queries;

public record GetTimeEntriesQuery(
    DateTime? From           = null,
    DateTime? To             = null,
    string?   Status         = null,
    Guid?     ProjectId      = null,
    Guid?     CollaboratorId = null) : IRequest<IReadOnlyList<TimeEntryDto>>;

public sealed class GetTimeEntriesQueryHandler
    : IRequestHandler<GetTimeEntriesQuery, IReadOnlyList<TimeEntryDto>>
{
    private readonly ITimeEntryRepository    _timeEntries;
    private readonly IProjectRepository      _projects;
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext          _tenant;

    public GetTimeEntriesQueryHandler(
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

    public async Task<IReadOnlyList<TimeEntryDto>> Handle(
        GetTimeEntriesQuery request,
        CancellationToken   cancellationToken)
    {
        var list = await _timeEntries.ListAsync(
            _tenant.OrganizationId,
            request.From,
            request.To,
            request.Status,
            request.ProjectId,
            request.CollaboratorId,
            cancellationToken);

        var projectList = await _projects.ListByOrganizationAsync(_tenant.OrganizationId, cancellationToken);
        var projectMap  = projectList.ToDictionary(p => p.Id, p => p.Name);

        var collList = await _collaborators.ListAsync(_tenant.OrganizationId, ct: cancellationToken);
        var collMap  = collList.ToDictionary(c => c.Id, c => c.Name);

        return list.Select(e => new TimeEntryDto(
            e.Id,
            e.ProjectId,
            projectMap.GetValueOrDefault(e.ProjectId, string.Empty),
            e.CollaboratorId,
            e.CollaboratorId.HasValue ? collMap.GetValueOrDefault(e.CollaboratorId.Value) : null,
            e.Description,
            e.Date,
            e.Hours,
            e.RateCost,
            e.RateBilled,
            e.Billable,
            e.Status,
            e.Currency,
            e.BilledAt,
            e.CreatedAt,
            e.UpdatedAt)).ToList();
    }
}
