using MediatR;
using Zentory.Application.Collaborators.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Collaborators.Queries;

public record GetCollaboratorsQuery(
    string? Search = null,
    string? Status = null) : IRequest<IReadOnlyList<CollaboratorSummaryDto>>;

public sealed class GetCollaboratorsQueryHandler
    : IRequestHandler<GetCollaboratorsQuery, IReadOnlyList<CollaboratorSummaryDto>>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly ITenantContext          _tenant;
    private readonly IFinancialVisibility    _visibility;

    public GetCollaboratorsQueryHandler(
        ICollaboratorRepository collaborators,
        ITenantContext          tenant,
        IFinancialVisibility    visibility)
    {
        _collaborators = collaborators;
        _tenant        = tenant;
        _visibility    = visibility;
    }

    public async Task<IReadOnlyList<CollaboratorSummaryDto>> Handle(
        GetCollaboratorsQuery request,
        CancellationToken     cancellationToken)
    {
        var list = await _collaborators.ListAsync(
            _tenant.OrganizationId,
            request.Search,
            request.Status,
            cancellationToken);

        return list.Select(c => new CollaboratorSummaryDto(
            c.Id,
            c.Name,
            c.Type,
            c.Status,
            c.Role,
            c.Email,
            // RF-86 d: member no recibe tarifas (D-29).
            _visibility.Amount(c.HourlyRate),
            _visibility.Amount(c.MonthlyRate),
            c.Currency,
            c.PilaStatus)).ToList();
    }
}
