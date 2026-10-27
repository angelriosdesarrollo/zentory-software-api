using MediatR;
using Zentory.Application.Collaborators.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Collaborators.Queries;

public record GetCollaboratorByIdQuery(Guid Id) : IRequest<CollaboratorDto>;

public sealed class GetCollaboratorByIdQueryHandler : IRequestHandler<GetCollaboratorByIdQuery, CollaboratorDto>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext          _tenant;
    private readonly IFinancialVisibility    _visibility;

    public GetCollaboratorByIdQueryHandler(
        ICollaboratorRepository collaborators,
        ITenantContext          tenant,
        IFinancialVisibility    visibility)
    {
        _collaborators = collaborators;
        _tenant        = tenant;
        _visibility    = visibility;
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
            // RF-86 d: member no recibe tarifas (D-29).
            _visibility.Amount(c.HourlyRate),
            _visibility.Amount(c.MonthlyRate),
            c.Currency,
            c.PilaStatus,
            c.PilaLastVerifiedPeriod,
            c.ArlRiskLevel,
            c.UserId,
            c.CreatedAt,
            c.UpdatedAt);
    }
}
