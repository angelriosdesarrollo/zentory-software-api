using MediatR;
using Zentory.Application.CollaboratorPortal.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Queries;

public sealed record GetOwnPilaHistoryQuery : IRequest<IReadOnlyList<OwnPilaVerificationDto>>;

public sealed class GetOwnPilaHistoryQueryHandler
    : IRequestHandler<GetOwnPilaHistoryQuery, IReadOnlyList<OwnPilaVerificationDto>>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ICollaboratorPortalContext  _portal;

    public GetOwnPilaHistoryQueryHandler(IPilaVerificationRepository verifications, ICollaboratorPortalContext portal)
    {
        _verifications = verifications;
        _portal        = portal;
    }

    public async Task<IReadOnlyList<OwnPilaVerificationDto>> Handle(
        GetOwnPilaHistoryQuery request, CancellationToken cancellationToken)
    {
        var list = await _verifications.ListByCollaboratorAsync(_portal.ActiveCollaboratorId, cancellationToken);
        return list
            .Select(v => new OwnPilaVerificationDto(
                v.Id, v.Period, v.Status, v.RequestedAt, v.ReceivedAt, v.VerifiedAt,
                v.DocumentFileName, v.DocumentFileSize, v.Source))
            .ToList();
    }
}
