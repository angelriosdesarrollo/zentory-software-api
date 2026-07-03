using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Confirma la subida de una cuenta de cobro desde el portal. Maneja 3 casos según la fila
// encontrada para (collaboratorId, period):
//   - no existe            → crear nueva, Source = "self_service"
//   - existe Source=manual_upload (la empresa la pidió) → MarkUploadedManually
//   - existe Source=generated    (la empresa la generó)  → MarkSigned (el colaborador subió
//     la versión firmada del borrador que la empresa generó)
public sealed record ConfirmOwnPayoutInvoiceCommand(
    string  Period,
    string  StorageKey,
    decimal DeclaredAmount,
    string? FileName    = null,
    long?   FileSize    = null,
    string? ContentType = null) : IRequest;

public sealed class ConfirmOwnPayoutInvoiceCommandValidator : AbstractValidator<ConfirmOwnPayoutInvoiceCommand>
{
    public ConfirmOwnPayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.Period).Matches(@"^\d{4}-\d{2}$")
            .WithMessage("Period must be in 'YYYY-MM' format.");
        RuleFor(x => x.StorageKey).NotEmpty();
        RuleFor(x => x.DeclaredAmount).GreaterThan(0);
    }
}

public sealed class ConfirmOwnPayoutInvoiceCommandHandler : IRequestHandler<ConfirmOwnPayoutInvoiceCommand>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository              _collaborators;
    private readonly ICollaboratorPortalContext           _portal;
    private readonly IUnitOfWork                          _uow;

    public ConfirmOwnPayoutInvoiceCommandHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository              collaborators,
        ICollaboratorPortalContext           portal,
        IUnitOfWork                          uow)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _portal        = portal;
        _uow           = uow;
    }

    public async Task Handle(ConfirmOwnPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var existing = await _invoices.GetByCollaboratorAndPeriodAsync(
            _portal.ActiveCollaboratorId, request.Period, cancellationToken);

        CollaboratorPayoutInvoice invoice;

        if (existing is null)
        {
            var collaborator = await _collaborators.GetByIdAsync(_portal.ActiveCollaboratorId, cancellationToken);
            invoice = CollaboratorPayoutInvoice.Create(
                _portal.ActiveOrganizationId, _portal.ActiveCollaboratorId, request.Period,
                concept: "Cuenta de cobro", amount: request.DeclaredAmount,
                currency: collaborator?.Currency ?? "COP", source: "self_service");
            invoice.MarkUploadedManually(
                request.StorageKey, request.DeclaredAmount, request.FileName, request.FileSize, request.ContentType);
            await _invoices.AddAsync(invoice, cancellationToken);
        }
        else if (existing.Source == "generated")
        {
            existing.MarkSigned(request.StorageKey, request.FileName, request.FileSize, request.ContentType);
            invoice = existing;
            await _invoices.UpdateAsync(invoice, cancellationToken);
        }
        else
        {
            existing.MarkUploadedManually(
                request.StorageKey, request.DeclaredAmount, request.FileName, request.FileSize, request.ContentType);
            invoice = existing;
            await _invoices.UpdateAsync(invoice, cancellationToken);
        }

        var updatedCollaborator = await _collaborators.GetByIdAsync(_portal.ActiveCollaboratorId, cancellationToken);
        if (updatedCollaborator is not null)
        {
            updatedCollaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
            await _collaborators.UpdateAsync(updatedCollaborator, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
    }
}
