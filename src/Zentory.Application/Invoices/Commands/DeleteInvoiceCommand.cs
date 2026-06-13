using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Invoices.Commands;

public record DeleteInvoiceCommand(Guid Id) : IRequest;

public sealed class DeleteInvoiceCommandHandler : IRequestHandler<DeleteInvoiceCommand>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IUnitOfWork        _uow;
    private readonly ITenantContext     _tenant;

    public DeleteInvoiceCommandHandler(IInvoiceRepository invoices, IUnitOfWork uow, ITenantContext tenant)
    {
        _invoices = invoices;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task Handle(DeleteInvoiceCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId || invoice.DeletedAt.HasValue)
            throw new NotFoundException("Invoice", request.Id);

        if (invoice.Status is "paid" or "sent" or "viewed")
            throw new ConflictException("INVOICE_CANNOT_DELETE", "No se puede eliminar una factura enviada o pagada.");

        invoice.SoftDelete();

        await _invoices.UpdateAsync(invoice, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
