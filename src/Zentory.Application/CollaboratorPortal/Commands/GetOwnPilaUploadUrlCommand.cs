using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PilaVerifications.DTOs;

namespace Zentory.Application.CollaboratorPortal.Commands;

public sealed record GetOwnPilaUploadUrlCommand(string ContentType) : IRequest<PresignedUploadUrlDto>;

public sealed class GetOwnPilaUploadUrlCommandHandler
    : IRequestHandler<GetOwnPilaUploadUrlCommand, PresignedUploadUrlDto>
{
    private readonly ICollaboratorPortalContext _portal;
    private readonly IStorageService            _storage;

    public GetOwnPilaUploadUrlCommandHandler(ICollaboratorPortalContext portal, IStorageService storage)
    {
        _portal  = portal;
        _storage = storage;
    }

    public async Task<PresignedUploadUrlDto> Handle(GetOwnPilaUploadUrlCommand request, CancellationToken cancellationToken)
    {
        var key = $"pila/{_portal.ActiveOrganizationId}/{_portal.ActiveCollaboratorId}/{Guid.NewGuid()}";
        var presigned = await _storage.GeneratePresignedUploadUrlAsync(
            key, request.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        return new PresignedUploadUrlDto(presigned.UploadUrl, presigned.Key, presigned.ExpiresAt);
    }
}
