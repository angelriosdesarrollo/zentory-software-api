using MediatR;
using Zentory.Application.CollaboratorPortal.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Queries;

public sealed record GetOwnPayoutInvoiceHistoryQuery : IRequest<IReadOnlyList<OwnPayoutInvoiceDto>>;

public sealed class GetOwnPayoutInvoiceHistoryQueryHandler
    : IRequestHandler<GetOwnPayoutInvoiceHistoryQuery, IReadOnlyList<OwnPayoutInvoiceDto>>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorPortalContext           _portal;

    public GetOwnPayoutInvoiceHistoryQueryHandler(
        ICollaboratorPayoutInvoiceRepository invoices, ICollaboratorPortalContext portal)
    {
        _invoices = invoices;
        _portal   = portal;
    }

    public async Task<IReadOnlyList<OwnPayoutInvoiceDto>> Handle(
        GetOwnPayoutInvoiceHistoryQuery request, CancellationToken cancellationToken)
    {
        var list = await _invoices.ListByCollaboratorAsync(_portal.ActiveCollaboratorId, cancellationToken);
        return list
            .Select(i => new OwnPayoutInvoiceDto(
                i.Id, i.Period, i.Concept, i.Amount, i.DeclaredAmount, i.Currency, i.Status, i.Source,
                i.DocumentFileName, i.DocumentFileSize, i.Notes, i.SignedByName, i.SignedAt))
            .ToList();
    }
}
