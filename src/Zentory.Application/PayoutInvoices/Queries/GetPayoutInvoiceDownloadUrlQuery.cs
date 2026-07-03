using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Queries;

public record GetPayoutInvoiceDownloadUrlQuery(Guid Id) : IRequest<string>;

public sealed class GetPayoutInvoiceDownloadUrlQueryHandler : IRequestHandler<GetPayoutInvoiceDownloadUrlQuery, string>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly IStorageService                       _storage;
    private readonly ITenantContext                        _tenant;

    public GetPayoutInvoiceDownloadUrlQueryHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        IStorageService                       storage,
        ITenantContext                        tenant)
    {
        _invoices = invoices;
        _storage  = storage;
        _tenant   = tenant;
    }

    public async Task<string> Handle(GetPayoutInvoiceDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (string.IsNullOrEmpty(invoice.StorageKey))
            throw new NotFoundException("CollaboratorPayoutInvoiceDocument", request.Id);

        return await _storage.GeneratePresignedDownloadUrlAsync(
            invoice.StorageKey, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
