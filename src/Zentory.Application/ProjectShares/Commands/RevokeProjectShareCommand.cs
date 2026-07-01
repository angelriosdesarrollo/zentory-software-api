using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.ProjectShares.Commands;

public record RevokeProjectShareCommand(Guid ShareId) : IRequest;

public sealed class RevokeProjectShareCommandHandler : IRequestHandler<RevokeProjectShareCommand>
{
    private readonly IProjectShareRepository _shares;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;

    public RevokeProjectShareCommandHandler(
        IProjectShareRepository shares,
        IUnitOfWork             uow,
        ITenantContext          tenant)
    {
        _shares = shares;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(RevokeProjectShareCommand request, CancellationToken cancellationToken)
    {
        var share = await _shares.GetByIdAsync(request.ShareId, cancellationToken);
        if (share is null || share.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("ProjectShare", request.ShareId);

        share.Revoke();
        await _shares.UpdateAsync(share, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
