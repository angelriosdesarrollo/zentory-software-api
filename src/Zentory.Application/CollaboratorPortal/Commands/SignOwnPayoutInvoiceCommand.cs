using FluentValidation;
using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Firma electrónica: el colaborador confirma con nombre escrito + checkbox (validado en el
// frontend) una cuenta de cobro que la empresa generó. No es un upload de archivo — el PDF se
// regenera con el mismo template + un bloque de firma, y se sube a un storage key nuevo (no
// sobreescribe el borrador original, por trazabilidad).
public sealed record SignOwnPayoutInvoiceCommand(Guid Id, string SignedByName) : IRequest;

public sealed class SignOwnPayoutInvoiceCommandValidator : AbstractValidator<SignOwnPayoutInvoiceCommand>
{
    public SignOwnPayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.SignedByName).NotEmpty().MaximumLength(200);
    }
}

public sealed class SignOwnPayoutInvoiceCommandHandler : IRequestHandler<SignOwnPayoutInvoiceCommand>
{
    // La empresa siempre llama a Send tras generar (notifica al colaborador por correo), lo
    // que mueve el Status de "generated" a "sent" — por eso ambos son firmables: el colaborador
    // casi nunca ve "generated" en la práctica, solo "sent".
    private static readonly string[] SignableStatuses = ["generated", "sent"];

    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository              _collaborators;
    private readonly IOrganizationBrandingResolver        _branding;
    private readonly IStorageService                      _storage;
    private readonly IPayoutInvoicePdfGenerator            _pdf;
    private readonly ICollaboratorPortalContext            _portal;
    private readonly IUnitOfWork                           _uow;

    public SignOwnPayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository              collaborators,
        IOrganizationBrandingResolver         branding,
        IStorageService                       storage,
        IPayoutInvoicePdfGenerator             pdf,
        ICollaboratorPortalContext            portal,
        IUnitOfWork                           uow)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _branding      = branding;
        _storage       = storage;
        _pdf           = pdf;
        _portal        = portal;
        _uow           = uow;
    }

    public async Task Handle(SignOwnPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.CollaboratorId != _portal.ActiveCollaboratorId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (invoice.Source != "generated")
            throw new ConflictException("NOT_SIGNABLE", "Esta cuenta de cobro no fue generada por la empresa.");

        if (!SignableStatuses.Contains(invoice.Status))
            throw new ConflictException("NOT_SIGNABLE", "Esta cuenta de cobro ya fue firmada o revisada.");

        var collaborator = await _collaborators.GetByIdAsync(_portal.ActiveCollaboratorId, cancellationToken);
        if (collaborator is null)
            throw new NotFoundException("Collaborator", _portal.ActiveCollaboratorId);

        var branding = await _branding.ResolveAsync(_portal.ActiveOrganizationId, cancellationToken);
        var signedAt = DateTime.UtcNow;

        var pdfBytes = _pdf.Generate(new PayoutInvoicePdfModel(
            _portal.ActiveOrganizationName,
            collaborator.Name,
            collaborator.IdNumber,
            invoice.Period,
            invoice.Concept,
            invoice.Amount,
            invoice.Currency,
            invoice.GeneratedAt ?? signedAt,
            LogoBytes: branding.LogoBytes,
            LegalName: branding.LegalName,
            Nit: branding.Nit,
            Address: branding.Address,
            City: branding.City,
            Email: branding.Email,
            Phone: branding.Phone,
            SignedByName: request.SignedByName,
            SignedAt: signedAt));

        var key      = StorageKeyBuilder.Build(
            _portal.ActiveOrganizationId, "payout-invoices", _portal.ActiveCollaboratorId,
            $"cuenta-cobro-{invoice.Period}-firmada", "application/pdf");
        var fileName = $"cuenta-cobro-{invoice.Period}-firmada.pdf";
        using (var stream = new MemoryStream(pdfBytes))
        {
            await _storage.UploadAsync(key, stream, "application/pdf", cancellationToken);
        }

        invoice.MarkSigned(key, fileName, pdfBytes.LongLength, "application/pdf", request.SignedByName);
        await _invoices.UpdateAsync(invoice, cancellationToken);

        collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
        await _collaborators.UpdateAsync(collaborator, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
    }
}
