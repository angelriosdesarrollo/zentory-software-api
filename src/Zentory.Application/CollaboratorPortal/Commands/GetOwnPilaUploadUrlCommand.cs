using FluentValidation;
using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PilaVerifications.DTOs;

namespace Zentory.Application.CollaboratorPortal.Commands;

public sealed record GetOwnPilaUploadUrlCommand(string Period, string ContentType) : IRequest<PresignedUploadUrlDto>;

public sealed class GetOwnPilaUploadUrlCommandValidator : AbstractValidator<GetOwnPilaUploadUrlCommand>
{
    public GetOwnPilaUploadUrlCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.ContentType).NotEmpty();
    }
}

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
        var key = StorageKeyBuilder.Build(
            _portal.ActiveOrganizationId, "pila", _portal.ActiveCollaboratorId,
            $"comprobante-pila-{request.Period}", request.ContentType);
        var presigned = await _storage.GeneratePresignedUploadUrlAsync(
            key, request.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        return new PresignedUploadUrlDto(presigned.UploadUrl, presigned.Key, presigned.ExpiresAt);
    }
}
