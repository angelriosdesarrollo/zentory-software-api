using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Queries;

// Descarga del borrador generado por la empresa (Source=generated), para que el
// colaborador pueda descargarlo, firmarlo fuera de la plataforma y subir la versión
// firmada — mismo documento, no requiere un token público aparte.
public sealed record GetOwnPayoutInvoiceDownloadUrlQuery(Guid Id) : IRequest<string>;

public sealed class GetOwnPayoutInvoiceDownloadUrlQueryHandler : IRequestHandler<GetOwnPayoutInvoiceDownloadUrlQuery, string>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorPortalContext           _portal;
    private readonly IStorageService                      _storage;

    public GetOwnPayoutInvoiceDownloadUrlQueryHandler(
        ICollaboratorPayoutInvoiceRepository invoices, ICollaboratorPortalContext portal, IStorageService storage)
    {
        _invoices = invoices;
        _portal   = portal;
        _storage  = storage;
    }

    public async Task<string> Handle(GetOwnPayoutInvoiceDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.GetByIdAsync(request.Id, cancellationToken);
        if (invoice is null || invoice.CollaboratorId != _portal.ActiveCollaboratorId)
            throw new NotFoundException("CollaboratorPayoutInvoice", request.Id);

        if (string.IsNullOrEmpty(invoice.StorageKey))
            throw new NotFoundException("CollaboratorPayoutInvoiceDocument", request.Id);

        return await _storage.GeneratePresignedDownloadUrlAsync(invoice.StorageKey, TimeSpan.FromMinutes(15), cancellationToken);
    }
}
