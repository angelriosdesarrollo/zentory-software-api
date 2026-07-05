using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects.Queries;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Projects.Commands;

/// <summary>Registra el archivo después de que el navegador lo subió con éxito al UploadUrl pre-firmado.</summary>
public record CreateProjectFileCommand(
    Guid   ProjectId,
    string Name,
    string FileType,
    string Size,
    string StorageKey) : IRequest<ProjectFileDto>;

public sealed class CreateProjectFileCommandValidator : AbstractValidator<CreateProjectFileCommand>
{
    public CreateProjectFileCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.FileType).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Size).NotEmpty();
        RuleFor(x => x.StorageKey).NotEmpty();
    }
}

public sealed class CreateProjectFileCommandHandler
    : IRequestHandler<CreateProjectFileCommand, ProjectFileDto>
{
    private readonly IZentoryDbContext  _db;
    private readonly IProjectRepository _projects;
    private readonly IUnitOfWork        _uow;
    private readonly ITenantContext     _tenant;

    public CreateProjectFileCommandHandler(
        IZentoryDbContext db, IProjectRepository projects, IUnitOfWork uow, ITenantContext tenant)
    {
        _db       = db;
        _projects = projects;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<ProjectFileDto> Handle(CreateProjectFileCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Project", request.ProjectId);

        var file = ProjectFile.Create(
            _tenant.OrganizationId,
            request.ProjectId,
            request.Name,
            request.FileType,
            request.Size,
            _tenant.UserInitials,
            request.StorageKey);

        _db.ProjectFiles.Add(file);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ProjectFileDto(
            file.Id, file.Name, file.FileType, file.Size, file.UploadedBy,
            file.UploadedAt.ToString("yyyy-MM-dd"), file.StorageKey);
    }
}
