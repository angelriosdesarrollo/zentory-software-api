using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.PilaVerifications.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Queries;

public record GetPilaVerificationDocumentsQuery(Guid PilaVerificationId) : IRequest<IReadOnlyList<PilaVerificationDocumentDto>>;

public sealed class GetPilaVerificationDocumentsQueryHandler
    : IRequestHandler<GetPilaVerificationDocumentsQuery, IReadOnlyList<PilaVerificationDocumentDto>>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ITenantContext              _tenant;

    public GetPilaVerificationDocumentsQueryHandler(IPilaVerificationRepository verifications, ITenantContext tenant)
    {
        _verifications = verifications;
        _tenant        = tenant;
    }

    public async Task<IReadOnlyList<PilaVerificationDocumentDto>> Handle(
        GetPilaVerificationDocumentsQuery request, CancellationToken cancellationToken)
    {
        var verification = await _verifications.GetByIdAsync(request.PilaVerificationId, cancellationToken);
        if (verification is null || verification.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("PilaVerification", request.PilaVerificationId);

        var documents = await _verifications.ListDocumentsAsync(request.PilaVerificationId, cancellationToken);

        return documents
            .Select(d => new PilaVerificationDocumentDto(
                d.Id, d.FileName, d.FileSize, d.ContentType, d.UploadedAt,
                DocumentRetentionRules.RetentionUntil(d.UploadedAt)))
            .ToList();
    }
}
