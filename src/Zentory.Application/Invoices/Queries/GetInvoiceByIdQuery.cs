using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Invoices.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Invoices.Queries;

public record GetInvoiceByIdQuery(Guid Id) : IRequest<InvoiceDto>;

public sealed class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, InvoiceDto>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IClientRepository  _clients;
    private readonly ITenantContext     _tenant;

    public GetInvoiceByIdQueryHandler(
        IInvoiceRepository invoices,
        IClientRepository  clients,
        ITenantContext     tenant)
    {
        _invoices = invoices;
        _clients  = clients;
        _tenant   = tenant;
    }

    public async Task<InvoiceDto> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdWithItemsAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId || invoice.DeletedAt.HasValue)
            throw new NotFoundException("Invoice", request.Id);

        var client = await _clients.GetByIdAsync(invoice.ClientId, cancellationToken);

        var items = invoice.Items.Select(item => new InvoiceItemDto(
            item.Id,
            item.Description,
            item.Quantity,
            item.UnitPrice,
            item.DiscountPct,
            item.Total,
            item.SortOrder)).ToList();

        return new InvoiceDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.ClientId,
            client?.Name ?? string.Empty,
            invoice.ProjectId,
            invoice.Status,
            invoice.DocumentType,
            invoice.IssuedAt,
            invoice.DueAt,
            invoice.Subtotal,
            invoice.TaxAmount,
            invoice.Total,
            invoice.AmountPaid,
            invoice.AmountDue,
            invoice.Currency,
            invoice.Notes,
            invoice.PaymentTerms,
            invoice.PaymentInstructions,
            items,
            invoice.CreatedAt,
            invoice.UpdatedAt);
    }
}
