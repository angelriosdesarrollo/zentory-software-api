using MediatR;
using Zentory.Application.Common;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.PayoutInvoices.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.PayoutInvoices.Queries;

public record GetPayoutInvoicesQuery(Guid CollaboratorId) : IRequest<IReadOnlyList<PayoutInvoiceDto>>;

public sealed class GetPayoutInvoicesQueryHandler
    : IRequestHandler<GetPayoutInvoicesQuery, IReadOnlyList<PayoutInvoiceDto>>
{
    private readonly ICollaboratorPayoutInvoiceRepository _invoices;
    private readonly ICollaboratorRepository               _collaborators;
    private readonly ITenantContext                        _tenant;

    public GetPayoutInvoicesQueryHandler(
        ICollaboratorPayoutInvoiceRepository invoices,
        ICollaboratorRepository               collaborators,
        ITenantContext                        tenant)
    {
        _invoices      = invoices;
        _collaborators = collaborators;
        _tenant        = tenant;
    }

    public async Task<IReadOnlyList<PayoutInvoiceDto>> Handle(
        GetPayoutInvoicesQuery request, CancellationToken cancellationToken)
    {
        var collaborator = await _collaborators.GetByIdAsync(request.CollaboratorId, cancellationToken);
        if (collaborator is null || collaborator.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Collaborator", request.CollaboratorId);

        var list = await _invoices.ListByCollaboratorAsync(request.CollaboratorId, cancellationToken);

        return list
            .Select(i => new PayoutInvoiceDto(
                i.Id, i.CollaboratorId, i.Period, i.Concept, i.Amount, i.DeclaredAmount, i.Currency,
                i.Status, i.Source, i.DocumentFileName, i.DocumentFileSize, i.GeneratedAt, i.SentAt, i.CreatedAt,
                // Solo hay documento (y por lo tanto retención) una vez generado/enviado/subido — 'draft' aún no tiene archivo.
                i.Status == "draft" ? null : DocumentRetentionRules.RetentionUntil(i.GeneratedAt ?? i.UpdatedAt)))
            .ToList();
    }
}
