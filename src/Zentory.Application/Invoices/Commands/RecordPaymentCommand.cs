using FluentValidation;
using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Invoices.Commands;

public record RecordPaymentCommand(Guid Id, decimal Amount) : IRequest;

public sealed class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class RecordPaymentCommandHandler : IRequestHandler<RecordPaymentCommand>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IUnitOfWork        _uow;
    private readonly ITenantContext     _tenant;

    public RecordPaymentCommandHandler(IInvoiceRepository invoices, IUnitOfWork uow, ITenantContext tenant)
    {
        _invoices = invoices;
        _uow      = uow;
        _tenant   = tenant;
    }

    public async Task Handle(RecordPaymentCommand request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId || invoice.DeletedAt.HasValue)
            throw new NotFoundException("Invoice", request.Id);

        if (invoice.Status is "paid" or "cancelled")
            throw new ConflictException("INVOICE_CLOSED", "La factura ya está pagada o cancelada.");

        invoice.RecordPayment(request.Amount);

        await _invoices.UpdateAsync(invoice, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
