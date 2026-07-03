using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Queries;

public record GetPilaVerificationDocumentDownloadUrlQuery(Guid DocumentId) : IRequest<string>;

public sealed class GetPilaVerificationDocumentDownloadUrlQueryHandler
    : IRequestHandler<GetPilaVerificationDocumentDownloadUrlQuery, string>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly IStorageService             _storage;
    private readonly ITenantContext              _tenant;

    public GetPilaVerificationDocumentDownloadUrlQueryHandler(
        IPilaVerificationRepository verifications, IStorageService storage, ITenantContext tenant)
    {
        _verifications = verifications;
        _storage       = storage;
        _tenant        = tenant;
    }

    public async Task<string> Handle(
        GetPilaVerificationDocumentDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var document = await _verifications.GetDocumentByIdAsync(request.DocumentId, cancellationToken);
        if (document is null)
            throw new NotFoundException("PilaVerificationDocument", request.DocumentId);

        var verification = await _verifications.GetByIdAsync(document.PilaVerificationId, cancellationToken);
        if (verification is null || verification.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("PilaVerificationDocument", request.DocumentId);

        return await _storage.GeneratePresignedDownloadUrlAsync(
            document.StorageKey, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
