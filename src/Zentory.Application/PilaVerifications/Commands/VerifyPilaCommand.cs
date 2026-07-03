using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Commands;

public record VerifyPilaCommand(
    Guid    Id,
    bool    Approved,
    string? Notes = null) : IRequest;

public sealed class VerifyPilaCommandValidator : AbstractValidator<VerifyPilaCommand>
{
    public VerifyPilaCommandValidator()
    {
        // Un rechazo sin motivo deja al colaborador sin saber qué corregir — se exige
        // el comentario para que quede en el histórico (ver PilaVerification.Notes).
        RuleFor(x => x.Notes)
            .NotEmpty()
            .When(x => !x.Approved)
            .WithMessage("Debes indicar el motivo del rechazo.");
    }
}

public sealed class VerifyPilaCommandHandler : IRequestHandler<VerifyPilaCommand>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ICollaboratorRepository     _collaborators;
    private readonly IOrganizationRepository     _organizations;
    private readonly IEmailService               _email;
    private readonly IApplicationSettings        _settings;
    private readonly IUnitOfWork                 _uow;
    private readonly ITenantContext              _tenant;

    public VerifyPilaCommandHandler(
        IPilaVerificationRepository verifications,
        ICollaboratorRepository     collaborators,
        IOrganizationRepository     organizations,
        IEmailService               email,
        IApplicationSettings        settings,
        IUnitOfWork                 uow,
        ITenantContext              tenant)
    {
        _verifications = verifications;
        _collaborators = collaborators;
        _organizations = organizations;
        _email         = email;
        _settings      = settings;
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

            await _verifications.UpdateAsync(verification, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        else
        {
            verification.Reject(request.Notes);
            // Regenera el token público (reusable, no de un solo uso) para garantizar que el
            // enlace del correo de rechazo no esté vencido, sin importar cuánto tiempo pasó
            // desde que se solicitó originalmente.
            verification.RegenerateToken();
            await _verifications.UpdateAsync(verification, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            var collaborator = await _collaborators.GetByIdAsync(verification.CollaboratorId, cancellationToken);
            if (collaborator is not null && !string.IsNullOrWhiteSpace(collaborator.Email))
            {
                var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);
                var portalUrl = $"{_settings.BaseUrl}/portal/entrar?token={verification.Token}&kind=pila_request";
                await _email.SendPilaRejectedEmailAsync(
                    collaborator.Email!, collaborator.Name, organization?.Name ?? string.Empty,
                    verification.Period, request.Notes!, portalUrl, cancellationToken);
            }
        }
    }
}
