using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PilaVerifications.DTOs;

namespace Zentory.Application.CollaboratorPortal.Commands;

public sealed record GetOwnPayoutInvoiceUploadUrlCommand(string ContentType) : IRequest<PresignedUploadUrlDto>;

public sealed class GetOwnPayoutInvoiceUploadUrlCommandHandler
    : IRequestHandler<GetOwnPayoutInvoiceUploadUrlCommand, PresignedUploadUrlDto>
{
    private readonly ICollaboratorPortalContext _portal;
    private readonly IStorageService            _storage;

    public GetOwnPayoutInvoiceUploadUrlCommandHandler(ICollaboratorPortalContext portal, IStorageService storage)
    {
        _portal  = portal;
        _storage = storage;
    }

    public async Task<PresignedUploadUrlDto> Handle(
        GetOwnPayoutInvoiceUploadUrlCommand request, CancellationToken cancellationToken)
    {
        var key = $"payout-invoices/{_portal.ActiveOrganizationId}/{_portal.ActiveCollaboratorId}/{Guid.NewGuid()}";
        var presigned = await _storage.GeneratePresignedUploadUrlAsync(
            key, request.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        return new PresignedUploadUrlDto(presigned.UploadUrl, presigned.Key, presigned.ExpiresAt);
    }
}
