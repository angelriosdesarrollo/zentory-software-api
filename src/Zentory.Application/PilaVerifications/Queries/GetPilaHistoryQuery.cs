using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.PilaVerifications.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Queries;

public record GetPilaHistoryQuery(Guid CollaboratorId) : IRequest<IReadOnlyList<PilaVerificationDto>>;

public sealed class GetPilaHistoryQueryHandler
    : IRequestHandler<GetPilaHistoryQuery, IReadOnlyList<PilaVerificationDto>>
{
    private readonly ICollaboratorRepository     _collaborators;
    private readonly IPilaVerificationRepository _verifications;
    private readonly ITenantContext              _tenant;

    public GetPilaHistoryQueryHandler(
        ICollaboratorRepository     collaborators,
        IPilaVerificationRepository verifications,
        ITenantContext              tenant)
    {
        _collaborators = collaborators;
        _verifications = verifications;
        _tenant        = tenant;
    }

    public async Task<IReadOnlyList<PilaVerificationDto>> Handle(
        GetPilaHistoryQuery request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.CollaboratorId, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Collaborator", request.CollaboratorId);

        var list = await _verifications.ListByCollaboratorAsync(request.CollaboratorId, cancellationToken);

        return list
            .Select(v => new PilaVerificationDto(
                v.Id, v.CollaboratorId, v.Period, v.Status, v.RequestedAt, v.ReceivedAt, v.VerifiedAt, v.Notes,
                v.DocumentFileName, v.DocumentFileSize, DocumentRetentionRules.RetentionUntil(v.ReceivedAt), v.Source))
            .ToList();
    }
}
