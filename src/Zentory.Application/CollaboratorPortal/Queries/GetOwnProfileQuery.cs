using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Queries;

public sealed record GetOwnProfileQuery : IRequest<OwnProfileDto>;

public sealed record OwnProfileDto(
    Guid     CollaboratorId,
    string   Name,
    string?  Email,
    string?  Role,
    string   Type,        // 'employee' | 'hourly_contractor' | 'fixed_contractor'
    decimal? HourlyRate,
    decimal? MonthlyRate,
    string   Currency,
    string   OrganizationName);

public sealed class GetOwnProfileQueryHandler : IRequestHandler<GetOwnProfileQuery, OwnProfileDto>
{
    private readonly ICollaboratorRepository    _collaborators;
    private readonly ICollaboratorPortalContext _portal;

    public GetOwnProfileQueryHandler(ICollaboratorRepository collaborators, ICollaboratorPortalContext portal)
    {
        _collaborators = collaborators;
        _portal        = portal;
    }

    public async Task<OwnProfileDto> Handle(GetOwnProfileQuery request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(_portal.ActiveCollaboratorId, cancellationToken);
        if (collaborator is null)
            throw new NotFoundException("Collaborator", _portal.ActiveCollaboratorId);

        return new OwnProfileDto(
            collaborator.Id,
            collaborator.Name,
            collaborator.Email,
            collaborator.Role,
            collaborator.Type,
            collaborator.HourlyRate,
            collaborator.MonthlyRate,
            collaborator.Currency,
            _portal.ActiveOrganizationName);
    }
}
