using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Queries;

public record GetPilaDownloadUrlQuery(Guid Id) : IRequest<string>;

public sealed class GetPilaDownloadUrlQueryHandler : IRequestHandler<GetPilaDownloadUrlQuery, string>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly IStorageService             _storage;
    private readonly ITenantContext              _tenant;

    public GetPilaDownloadUrlQueryHandler(
        IPilaVerificationRepository verifications,
        IStorageService             storage,
        ITenantContext              tenant)
    {
        _verifications = verifications;
        _storage       = storage;
        _tenant        = tenant;
    }

    public async Task<string> Handle(GetPilaDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var verification = await _verifications.GetByIdAsync(request.Id, cancellationToken);
        if (verification is null || verification.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("PilaVerification", request.Id);

        if (string.IsNullOrEmpty(verification.DocumentUrl))
            throw new NotFoundException("PilaDocument", request.Id);

        return await _storage.GeneratePresignedDownloadUrlAsync(
            verification.DocumentUrl, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
