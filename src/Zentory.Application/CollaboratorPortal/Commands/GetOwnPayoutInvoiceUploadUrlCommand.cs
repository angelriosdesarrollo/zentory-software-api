using FluentValidation;
using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PilaVerifications.DTOs;

namespace Zentory.Application.CollaboratorPortal.Commands;

public sealed record GetOwnPayoutInvoiceUploadUrlCommand(string Period, string ContentType) : IRequest<PresignedUploadUrlDto>;

public sealed class GetOwnPayoutInvoiceUploadUrlCommandValidator : AbstractValidator<GetOwnPayoutInvoiceUploadUrlCommand>
{
    public GetOwnPayoutInvoiceUploadUrlCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.ContentType).NotEmpty();
    }
}

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
        var key = StorageKeyBuilder.Build(
            _portal.ActiveOrganizationId, "payout-invoices", _portal.ActiveCollaboratorId,
            $"cuenta-cobro-{request.Period}", request.ContentType);
        var presigned = await _storage.GeneratePresignedUploadUrlAsync(
            key, request.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        return new PresignedUploadUrlDto(presigned.UploadUrl, presigned.Key, presigned.ExpiresAt);
    }
}
