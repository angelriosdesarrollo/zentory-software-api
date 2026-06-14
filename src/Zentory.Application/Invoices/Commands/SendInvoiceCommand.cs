using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Invoices.Commands;

public record SendInvoiceCommand(Guid Id) : IRequest;

public sealed class SendInvoiceCommandHandler : IRequestHandler<SendInvoiceCommand>
{
    private readonly IInvoiceRepository  _invoices;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public SendInvoiceCommandHandler(
        IInvoiceRepository  invoices,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _invoices    = invoices;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId || invoice.DeletedAt.HasValue)
            throw new NotFoundException("Invoice", request.Id);

        if (invoice.Status != "draft")
            throw new ConflictException("INVOICE_NOT_DRAFT", "Solo las facturas en borrador pueden enviarse.");

        invoice.MarkAsSent();

        await _invoices.UpdateAsync(invoice, cancellationToken);

        await _activityLog.LogAsync(
            "Invoice",
            invoice.Id,
            $"Envió la factura {invoice.InvoiceNumber} al cliente",
            invoice.InvoiceNumber,
            ct: cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
    }
}
