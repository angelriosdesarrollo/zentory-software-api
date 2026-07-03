using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Commands;

public record SendPayoutInvoiceCommand(Guid Id) : IRequest;

public sealed class SendPayoutInvoiceCommandHandler : IRequestHandler<SendPayoutInvoiceCommand>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository               _collaborators;
    private readonly IOrganizationRepository               _organizations;
    private readonly IStorageService                       _storage;
    private readonly IEmailService                         _email;
    private readonly IUnitOfWork                            _uow;
    private readonly ITenantContext                         _tenant;
    private readonly IApplicationSettings                   _settings;

    public SendPayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository               collaborators,
        IOrganizationRepository               organizations,
        IStorageService                       storage,
        IEmailService                         email,
        IUnitOfWork                           uow,
        ITenantContext                        tenant,
        IApplicationSettings                  settings)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _organizations = organizations;
        _storage       = storage;
        _email         = email;
        _uow           = uow;
        _tenant        = tenant;
        _settings      = settings;
    }

    public async Task Handle(SendPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (string.IsNullOrEmpty(invoice.StorageKey))
            throw new ConflictException("INVOICE_NOT_GENERATED", "La cuenta de cobro aún no tiene documento generado.");

        var collaborator = await _collaborators.GetByIdAsync(invoice.CollaboratorId, cancellationToken);
        if (collaborator is null || string.IsNullOrWhiteSpace(collaborator.Email))
            throw new ConflictException("NO_EMAIL", "El colaborador no tiene correo registrado.");

        var organization = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);

        var downloadUrl = await _storage.GeneratePresignedDownloadUrlAsync(
            invoice.StorageKey, TimeSpan.FromDays(7), cancellationToken);
        var portalUrl = $"{_settings.BaseUrl}/portal/entrar?token={invoice.PublicToken}&kind=payout_invoice_request";

        await _email.SendPayoutInvoiceGeneratedEmailAsync(
            collaborator.Email!,
            collaborator.Name,
            organization?.Name ?? string.Empty,
            invoice.Period,
            invoice.Amount,
            invoice.Currency,
            downloadUrl,
            portalUrl,
            cancellationToken);

        invoice.MarkSent();
        collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);

        await _invoices.UpdateAsync(invoice, cancellationToken);
        await _collaborators.UpdateAsync(collaborator, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
