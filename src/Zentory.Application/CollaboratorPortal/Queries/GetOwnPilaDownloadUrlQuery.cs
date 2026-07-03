using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Queries;

public sealed record GetOwnPilaDownloadUrlQuery(Guid Id) : IRequest<string>;

public sealed class GetOwnPilaDownloadUrlQueryHandler : IRequestHandler<GetOwnPilaDownloadUrlQuery, string>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ICollaboratorPortalContext  _portal;
    private readonly IStorageService             _storage;

    public GetOwnPilaDownloadUrlQueryHandler(
        IPilaVerificationRepository verifications, ICollaboratorPortalContext portal, IStorageService storage)
    {
        _verifications = verifications;
        _portal        = portal;
        _storage       = storage;
    }

    public async Task<string> Handle(GetOwnPilaDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var verification = await _verifications.GetByIdAsync(request.Id, cancellationToken);
        if (verification is null || verification.CollaboratorId != _portal.ActiveCollaboratorId)
            throw new NotFoundException("PilaVerification", request.Id);

        if (string.IsNullOrEmpty(verification.DocumentUrl))
            throw new NotFoundException("PilaVerificationDocument", request.Id);

        return await _storage.GeneratePresignedDownloadUrlAsync(verification.DocumentUrl, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
