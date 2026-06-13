using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Invoices.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Invoices.Queries;

public record GetInvoicesQuery(
    string? Status   = null,
    Guid?   ClientId = null) : IRequest<IReadOnlyList<InvoiceSummaryDto>>;

public sealed class GetInvoicesQueryHandler
    : IRequestHandler<GetInvoicesQuery, IReadOnlyList<InvoiceSummaryDto>>
{
    private readonly IInvoiceRepository _invoices;
    private readonly IClientRepository  _clients;
    private readonly ITenantContext     _tenant;

    public GetInvoicesQueryHandler(
        IInvoiceRepository invoices,
        IClientRepository  clients,
        ITenantContext     tenant)
    {
        _invoices = invoices;
        _clients  = clients;
        _tenant   = tenant;
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> Handle(
        GetInvoicesQuery  request,
        CancellationToken cancellationToken)
    {
        var list = await _invoices.ListAsync(
            _tenant.OrganizationId,
            request.Status,
            request.ClientId,
            cancellationToken);

        var clientList = await _clients.ListAsync(_tenant.OrganizationId, ct: cancellationToken);
        var clientMap  = clientList.ToDictionary(c => c.Id, c => c.Name);

        return list.Select(i => new InvoiceSummaryDto(
            i.Id,
            i.InvoiceNumber,
            i.ClientId,
            clientMap.GetValueOrDefault(i.ClientId, string.Empty),
            i.ProjectId,
            i.Status,
            i.DocumentType,
            i.IssuedAt,
            i.DueAt,
            i.Total,
            i.AmountPaid,
            i.AmountDue,
            i.Currency)).ToList();
    }
}
