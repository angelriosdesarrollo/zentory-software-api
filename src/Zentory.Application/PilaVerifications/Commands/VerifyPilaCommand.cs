using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Commands;

public record VerifyPilaCommand(
    Guid    Id,
    bool    Approved,
    string? Notes = null) : IRequest;

public sealed class VerifyPilaCommandHandler : IRequestHandler<VerifyPilaCommand>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ICollaboratorRepository     _collaborators;
    private readonly IUnitOfWork                 _uow;
    private readonly ITenantContext              _tenant;

    public VerifyPilaCommandHandler(
        IPilaVerificationRepository verifications,
        ICollaboratorRepository     collaborators,
        IUnitOfWork                 uow,
        ITenantContext              tenant)
    {
        _verifications = verifications;
        _collaborators = collaborators;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task Handle(VerifyPilaCommand request, CancellationToken cancellationToken)
    {
        var verification = await _verifications.GetByIdAsync(request.Id, cancellationToken);
        if (verification is null || verification.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("PilaVerification", request.Id);

        if (request.Approved)
        {
            verification.MarkVerified();

            var collaborator = await _collaborators.GetByIdAsync(verification.CollaboratorId, cancellationToken);
            if (collaborator is not null)
            {
                collaborator.UpdatePilaStatus("verificada", verification.Period);
                await _collaborators.UpdateAsync(collaborator, cancellationToken);
            }
        }
        else
        {
            verification.Reject(request.Notes);
        }

        await _verifications.UpdateAsync(verification, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
