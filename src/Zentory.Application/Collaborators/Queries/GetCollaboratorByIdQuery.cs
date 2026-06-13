using MediatR;
using Zentory.Application.Collaborators.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Collaborators.Queries;

public record GetCollaboratorByIdQuery(Guid Id) : IRequest<CollaboratorDto>;

public sealed class GetCollaboratorByIdQueryHandler : IRequestHandler<GetCollaboratorByIdQuery, CollaboratorDto>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext          _tenant;

    public GetCollaboratorByIdQueryHandler(ICollaboratorRepository collaborators, ITenantContext tenant)
    {
        _collaborators = collaborators;
        _tenant        = tenant;
    }

    public async Task<CollaboratorDto> Handle(GetCollaboratorByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _collaborators.GetByIdAsync(request.Id, cancellationToken);
        if (c is null || c.OrganizationId != _tenant.OrganizationId || c.DeletedAt.HasValue)
            throw new NotFoundException("Collaborator", request.Id);

        return new CollaboratorDto(
            c.Id,
            c.Name,
            c.Type,
            c.Status,
            c.Role,
            c.Email,
            c.Phone,
            c.IdNumber,
            c.HourlyRate,
            c.MonthlyRate,
            c.Currency,
            c.PilaStatus,
            c.PilaLastVerifiedPeriod,
            c.ArlRiskLevel,
            c.UserId,
            c.CreatedAt,
            c.UpdatedAt);
    }
}
