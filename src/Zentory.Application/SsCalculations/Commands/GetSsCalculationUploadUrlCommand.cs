using FluentValidation;
using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.SsCalculations.DTOs;

namespace Zentory.Application.SsCalculations.Commands;

public sealed record GetSsCalculationUploadUrlCommand(string Period, string ContentType)
    : IRequest<SsCalculationUploadUrlDto>;

public sealed class GetSsCalculationUploadUrlCommandValidator : AbstractValidator<GetSsCalculationUploadUrlCommand>
{
    public GetSsCalculationUploadUrlCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.ContentType).NotEmpty();
    }
}

public sealed class GetSsCalculationUploadUrlCommandHandler
    : IRequestHandler<GetSsCalculationUploadUrlCommand, SsCalculationUploadUrlDto>
{
    private readonly ITenantContext  _tenant;
    private readonly IStorageService _storage;

    public GetSsCalculationUploadUrlCommandHandler(ITenantContext tenant, IStorageService storage)
    {
        _tenant  = tenant;
        _storage = storage;
    }

    public async Task<SsCalculationUploadUrlDto> Handle(
        GetSsCalculationUploadUrlCommand request, CancellationToken ct)
    {
        var key = StorageKeyBuilder.Build(
            _tenant.OrganizationId, "pila-personal", _tenant.UserId,
            $"comprobante-pila-{request.Period}", request.ContentType);
        var presigned = await _storage.GeneratePresignedUploadUrlAsync(
            key, request.ContentType, TimeSpan.FromMinutes(15), ct);

        return new SsCalculationUploadUrlDto(presigned.UploadUrl, presigned.Key, presigned.ExpiresAt);
    }
}
