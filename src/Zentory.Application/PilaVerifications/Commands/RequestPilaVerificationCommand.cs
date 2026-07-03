using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PilaVerifications.Commands;

public record RequestPilaVerificationCommand(
    IReadOnlyList<Guid> CollaboratorIds,
    string              Period) : IRequest<RequestPilaVerificationResult>;

public record RequestPilaVerificationResult(int Requested, int Skipped);

public sealed class RequestPilaVerificationCommandValidator : AbstractValidator<RequestPilaVerificationCommand>
{
    public RequestPilaVerificationCommandValidator()
    {
        RuleFor(x => x.CollaboratorIds).NotEmpty();
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
    }
}

public sealed class RequestPilaVerificationCommandHandler
    : IRequestHandler<RequestPilaVerificationCommand, RequestPilaVerificationResult>
{
    private readonly IPilaVerificationRepository _verifications;
    private readonly ICollaboratorRepository     _collaborators;
    private readonly IOrganizationRepository     _organizations;
    private readonly IUnitOfWork                 _uow;
    private readonly ITenantContext              _tenant;
    private readonly IEmailService               _email;
    private readonly IApplicationSettings        _settings;

    public RequestPilaVerificationCommandHandler(
        IPilaVerificationRepository verifications,
        ICollaboratorRepository     collaborators,
        IOrganizationRepository     organizations,
        IUnitOfWork                 uow,
        ITenantContext              tenant,
        IEmailService               email,
        IApplicationSettings        settings)
    {
        _verifications = verifications;
        _collaborators = collaborators;
        _organizations = organizations;
        _uow           = uow;
        _tenant        = tenant;
        _email         = email;
        _settings      = settings;
    }

    public async Task<RequestPilaVerificationResult> Handle(
        RequestPilaVerificationCommand request, CancellationToken cancellationToken)
    {
        var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);
        var companyName  = organization?.Name ?? string.Empty;

        var toNotify = new List<(Collaborator Collaborator, PilaVerification Verification)>();
        var skipped  = 0;

        foreach (var collaboratorId in request.CollaboratorIds.Distinct())
        {
            var collaborator = await _collaborators.GetByIdAsync(collaboratorId, cancellationToken);
            if (collaborator is null
                || collaborator.OrganizationId != _tenant.OrganizationId
                || collaborator.DeletedAt.HasValue
                || collaborator.Type == "employee"
                || string.IsNullOrWhiteSpace(collaborator.Email))
            {
                skipped++;
                continue;
            }

            var verification = await _verifications.GetByCollaboratorAndPeriodAsync(
                collaboratorId, request.Period, cancellationToken);

            if (verification is null)
            {
                verification = PilaVerification.Create(
                    _tenant.OrganizationId, collaboratorId, request.Period, _tenant.UserId);
                await _verifications.AddAsync(verification, cancellationToken);
            }
            else
            {
                verification.RegenerateToken();
                await _verifications.UpdateAsync(verification, cancellationToken);
            }

            collaborator.UpdatePilaStatus("solicitada", collaborator.PilaLastVerifiedPeriod);
            await _collaborators.UpdateAsync(collaborator, cancellationToken);

            toNotify.Add((collaborator, verification));
        }

        await _uow.SaveChangesAsync(cancellationToken);

        foreach (var (collaborator, verification) in toNotify)
        {
            var url = $"{_settings.BaseUrl}/portal/entrar?token={verification.Token}&kind=pila_request";
            await _email.SendPilaRequestEmailAsync(
                collaborator.Email!,
                collaborator.Name,
                companyName,
                request.Period,
                url,
                verification.TokenExpiresAt ?? DateTime.UtcNow.AddDays(30),
                cancellationToken);
        }

        return new RequestPilaVerificationResult(toNotify.Count, skipped);
    }
}
