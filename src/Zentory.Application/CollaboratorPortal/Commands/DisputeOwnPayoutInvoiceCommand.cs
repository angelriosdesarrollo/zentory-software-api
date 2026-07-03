using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Dirección opuesta a ReviewPayoutInvoiceCommand (Approved=false): acá es el colaborador
// quien rechaza una cuenta de cobro que la empresa generó, en vez de firmarla. Exige motivo
// igual que cuando la empresa rechaza — la empresa lo ve en su historial y corrige generando
// una cuenta de cobro nueva (ya soportado: una empresa puede generar varias por mes).
public sealed record DisputeOwnPayoutInvoiceCommand(Guid Id, string Notes) : IRequest;

public sealed class DisputeOwnPayoutInvoiceCommandValidator : AbstractValidator<DisputeOwnPayoutInvoiceCommand>
{
    public DisputeOwnPayoutInvoiceCommandValidator()
    {
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(1000);
    }
}

public sealed class DisputeOwnPayoutInvoiceCommandHandler : IRequestHandler<DisputeOwnPayoutInvoiceCommand>
{
    // Mismos estados firmables que SignOwnPayoutInvoiceCommand — la empresa siempre llama a
    // Send tras generar, así que en la práctica el colaborador casi siempre ve "sent".
    private static readonly string[] DisputableStatuses = ["generated", "sent"];

    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository              _collaborators;
    private readonly ICollaboratorPortalContext           _portal;
    private readonly IUnitOfWork                          _uow;

    public DisputeOwnPayoutInvoiceCommandHandler(
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

    public async Task Handle(DisputeOwnPayoutInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.CollaboratorId != _portal.ActiveCollaboratorId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (invoice.Source != "generated")
            throw new ConflictException("NOT_DISPUTABLE", "Esta cuenta de cobro no fue generada por la empresa.");

        if (!DisputableStatuses.Contains(invoice.Status))
            throw new ConflictException("NOT_DISPUTABLE", "Esta cuenta de cobro ya fue firmada o revisada.");

        invoice.Dispute(request.Notes);
        await _invoices.UpdateAsync(invoice, cancellationToken);

        var collaborator = await _collaborators.GetByIdAsync(_portal.ActiveCollaboratorId, cancellationToken);
        if (collaborator is not null)
        {
            collaborator.UpdatePayoutInvoiceStatus(invoice.Status, invoice.Period);
            await _collaborators.UpdateAsync(collaborator, cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
    }
}
