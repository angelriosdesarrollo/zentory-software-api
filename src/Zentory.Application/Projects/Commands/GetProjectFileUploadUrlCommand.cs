using FluentValidation;
using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.PilaVerifications.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

public record GetProjectFileUploadUrlCommand(
    Guid   ProjectId,
    string FileName,
    string ContentType) : IRequest<PresignedUploadUrlDto>;

public sealed class GetProjectFileUploadUrlCommandValidator : AbstractValidator<GetProjectFileUploadUrlCommand>
{
    public GetProjectFileUploadUrlCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ContentType).NotEmpty();
    }
}

public sealed class GetProjectFileUploadUrlCommandHandler
    : IRequestHandler<GetProjectFileUploadUrlCommand, PresignedUploadUrlDto>
{
    private readonly IProjectRepository _projects;
    private readonly IStorageService    _storage;
    private readonly ITenantContext     _tenant;

    public GetProjectFileUploadUrlCommandHandler(
        IProjectRepository projects, IStorageService storage, ITenantContext tenant)
    {
        _projects = projects;
        _storage  = storage;
        _tenant   = tenant;
    }

    public async Task<PresignedUploadUrlDto> Handle(
        GetProjectFileUploadUrlCommand request,
        CancellationToken              cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Project", request.ProjectId);

        var slug = Path.GetFileNameWithoutExtension(request.FileName);
        var key  = StorageKeyBuilder.Build(_tenant.OrganizationId, "projects", request.ProjectId, slug, request.ContentType);

        var presigned = await _storage.GeneratePresignedUploadUrlAsync(
            key, request.ContentType, TimeSpan.FromMinutes(15), cancellationToken);

        return new PresignedUploadUrlDto(presigned.UploadUrl, presigned.Key, presigned.ExpiresAt);
    }
}
