using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Commands;

// Mismo patrón que VerifyPilaCommand: la empresa aprueba o rechaza una cuenta de cobro
// que ya tiene documento (generada+firmada, o subida). Un rechazo exige motivo.
public record ReviewPayoutInvoiceCommand(
    Guid    Id,
    bool    Approved,
    string? Notes = null) : IRequest;

public sealed class ReviewPayoutInvoiceCommandValidator : AbstractValidator<ReviewPayoutInvoiceCommand>
{
    public ReviewPayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.Notes)
            .NotEmpty()
            .When(x => !x.Approved)
            .WithMessage("Debes indicar el motivo del rechazo.");
    }
}

public sealed class ReviewPayoutInvoiceCommandHandler : IRequestHandler<ReviewPayoutInvoiceCommand>
{
    private static readonly string[] ReviewableStatuses = ["generated", "signed", "uploaded_manually"];

    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository               _collaborators;
    private readonly IOrganizationRepository               _organizations;
    private readonly IEmailService                         _email;
    private readonly IApplicationSettings                  _settings;
    private readonly IUnitOfWork                            _uow;
    private readonly ITenantContext                         _tenant;

    public ReviewPayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository               collaborators,
        IOrganizationRepository               organizations,
        IEmailService                         email,
        IApplicationSettings                  settings,
        IUnitOfWork                           uow,
        ITenantContext                        tenant)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _organizations = organizations;
        _email         = email;
        _settings      = settings;
        _uow           = uow;
        _tenant        = tenant;
    }

    public async Task Handle(ReviewPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (!ReviewableStatuses.Contains(invoice.Status))
            throw new ConflictException("NOT_REVIEWABLE", "Esta cuenta de cobro todavía no tiene documento para revisar.");

        if (request.Approved)
        {
            invoice.Approve();
            await _invoices.UpdateAsync(invoice, cancellationToken);

            var collaborator = await _collaborators.GetByIdAsync(invoice.CollaboratorId, cancellationToken);
            if (collaborator is not null)
            {
                collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
                await _collaborators.UpdateAsync(collaborator, cancellationToken);
            }

            await _uow.SaveChangesAsync(cancellationToken);
        }
        else
        {
            invoice.Reject(request.Notes!);
            // Mismo motivo que en PILA: token reusable regenerado para que el enlace del
            // correo de rechazo nunca esté vencido.
            invoice.RegenerateToken();
            await _invoices.UpdateAsync(invoice, cancellationToken);

            var collaborator = await _collaborators.GetByIdAsync(invoice.CollaboratorId, cancellationToken);
            if (collaborator is not null)
            {
                collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
                await _collaborators.UpdateAsync(collaborator, cancellationToken);
            }

            await _uow.SaveChangesAsync(cancellationToken);

            if (collaborator is not null && !string.IsNullOrWhiteSpace(collaborator.Email))
            {
                var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);
                var portalUrl = $"{_settings.BaseUrl}/portal/entrar?token={invoice.PublicToken}&kind=payout_invoice_request";
                await _email.SendPayoutInvoiceRejectedEmailAsync(
                    collaborator.Email!, collaborator.Name, organization?.Name ?? string.Empty,
                    invoice.Period, request.Notes!, portalUrl, cancellationToken);
            }
        }
    }
}
