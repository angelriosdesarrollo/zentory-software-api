using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Collaborators.Commands;

public record DeleteCollaboratorCommand(Guid Id) : IRequest;

public sealed class DeleteCollaboratorCommandHandler : IRequestHandler<DeleteCollaboratorCommand>
{
    private readonly ICollaboratorRepository _collaborators;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;

    public DeleteCollaboratorCommandHandler(
        ICollaboratorRepository collaborators,
        IUnitOfWork             uow,
        ITenantContext          tenant)
    {
        _collaborators = collaborators;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task Handle(DeleteCollaboratorCommand request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.Id, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId || collaborator.DeletedAt.HasValue)
            throw new NotFoundException("Collaborator", request.Id);

        collaborator.SoftDelete();

        await _collaborators.UpdateAsync(collaborator, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
