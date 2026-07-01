using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.ProjectShares.DTOs;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectShares.Commands;

public record CreateProjectShareCommand(
    Guid      ProjectId,
    string?   Message,
    DateTime? ExpiresAt,
    string[]  IncludedFileIds,
    string[]  IncludedDeliverableIds) : IRequest<ProjectShareDto>;

public sealed class CreateProjectShareCommandHandler : IRequestHandler<CreateProjectShareCommand, ProjectShareDto>
{
    private readonly IProjectShareRepository _shares;
    private readonly IProjectRepository      _projects;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;

    public CreateProjectShareCommandHandler(
        IProjectShareRepository shares,
        IProjectRepository      projects,
        IUnitOfWork             uow,
        ITenantContext          tenant)
    {
        _shares   = shares;
        _projects = projects;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task<ProjectShareDto> Handle(CreateProjectShareCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null || project.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Project", request.ProjectId);

        var token = GenerateToken();

        var share = ProjectShare.Create(
            _tenant.OrganizationId,
            request.ProjectId,
            _tenant.UserId,
            token,
            request.Message,
            request.ExpiresAt,
            request.IncludedFileIds,
            request.IncludedDeliverableIds);

        await _shares.AddAsync(share, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return ToDto(share);
    }

    private static string GenerateToken()
    {
        var bytes = new byte[24];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    internal static ProjectShareDto ToDto(ProjectShare s) => new(
        s.Id,
        s.Token,
        s.ProjectId,
        s.CreatedBy,
        s.CreatedAt,
        s.ExpiresAt,
        s.Message,
        s.IncludedFileIds,
        s.IncludedDeliverableIds);
}
